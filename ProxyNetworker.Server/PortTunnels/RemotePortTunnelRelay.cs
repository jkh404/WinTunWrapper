using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.Json;
using ProxyNetworker.Core.PortTunnels.Remote;
using ProxyNetworker.Server.Data;
using ProxyNetworker.Server.Management;

namespace ProxyNetworker.Server.PortTunnels;

public sealed class RemotePortTunnelRelay : IDisposable
{
    private const int BufferSize = 64 * 1024;
    private static readonly TimeSpan UdpSessionIdleTimeout = TimeSpan.FromMinutes(2);

    private readonly PortTunnelDefinitionRecord _definition;
    private readonly ILogger<RemotePortTunnelRelay> _logger;
    private readonly TrafficLimiter _trafficLimiter;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly ConcurrentDictionary<ulong, TcpClient> _tcpConnections = new();
    private readonly ConcurrentDictionary<string, UdpPublicSession> _udpSessionsByPeer = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<ulong, UdpPublicSession> _udpSessionsById = new();
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private CancellationTokenSource? _runtimeCancellation;
    private TcpListener? _tcpListener;
    private UdpClient? _udpSocket;
    private WebSocket? _webSocket;
    private Task? _publicLoop;
    private long _nextConnectionId;
    private int _disposed;

    public RemotePortTunnelRelay(PortTunnelDefinitionRecord definition, ILogger<RemotePortTunnelRelay> logger)
    {
        _definition = definition;
        _logger = logger;
        _trafficLimiter = new TrafficLimiter(definition.BandwidthLimitBytes, definition.MaxTrafficSpeedBytesPerSecond);
    }

    public async Task RunAsync(WebSocket webSocket, CancellationToken cancellationToken)
    {
        if (webSocket is null)
        {
            throw new ArgumentNullException(nameof(webSocket));
        }

        _webSocket = webSocket;
        _runtimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var runtimeToken = _runtimeCancellation.Token;

        StartPublicEndpoint(runtimeToken);
        await SendReadyAsync(runtimeToken).ConfigureAwait(false);
        _logger.LogInformation(
            "Remote port tunnel {TunnelName} connected. protocol={Protocol}, public=0.0.0.0:{PublicPort}, private={PrivateHost}:{PrivatePort}.",
            _definition.Name,
            _definition.Protocol,
            _definition.PublicPort,
            _definition.PrivateHost,
            _definition.PrivatePort);

        await ReceiveClientLoopAsync(runtimeToken).ConfigureAwait(false);
    }

    public TunnelSummary ToSummary()
    {
        return new TunnelSummary(
            _definition.Id,
            _definition.Name,
            "remote-port-tunnel",
            _definition.Protocol,
            $"0.0.0.0:{_definition.PublicPort}",
            $"{_definition.PrivateHost}:{_definition.PrivatePort}",
            _startedAt,
            _webSocket?.State == WebSocketState.Open ? "connected" : "stopping");
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _runtimeCancellation?.Cancel();
        _tcpListener?.Stop();
        _udpSocket?.Dispose();

        foreach (var item in _tcpConnections.ToArray())
        {
            if (_tcpConnections.TryRemove(item.Key, out var client))
            {
                client.Dispose();
            }
        }

        foreach (var item in _udpSessionsById.ToArray())
        {
            _udpSessionsById.TryRemove(item.Key, out _);
        }

        _udpSessionsByPeer.Clear();
        _runtimeCancellation?.Dispose();
        _sendLock.Dispose();
    }

    private void StartPublicEndpoint(CancellationToken cancellationToken)
    {
        if (string.Equals(_definition.Protocol, "tcp", StringComparison.OrdinalIgnoreCase))
        {
            _tcpListener = new TcpListener(IPAddress.Any, _definition.PublicPort);
            _tcpListener.Start();
            _publicLoop = Task.Run(() => AcceptTcpLoopAsync(cancellationToken), CancellationToken.None);
            return;
        }

        if (string.Equals(_definition.Protocol, "udp", StringComparison.OrdinalIgnoreCase))
        {
            _udpSocket = new UdpClient(new IPEndPoint(IPAddress.Any, _definition.PublicPort));
            _publicLoop = Task.Run(() => ReceiveUdpPublicLoopAsync(cancellationToken), CancellationToken.None);
            return;
        }

        throw new InvalidOperationException($"Unsupported port tunnel protocol '{_definition.Protocol}'.");
    }

    private async Task SendReadyAsync(CancellationToken cancellationToken)
    {
        var config = new RemotePortTunnelConfig
        {
            TunnelId = _definition.Id,
            Name = _definition.Name,
            Protocol = _definition.Protocol,
            PrivateHost = _definition.PrivateHost,
            PrivatePort = _definition.PrivatePort,
            PublicPort = _definition.PublicPort
        };
        var json = JsonSerializer.Serialize(config);
        await SendToClientAsync(PortTunnelRelayFrame.Text(PortTunnelRelayFrameType.Ready, 0, json), cancellationToken).ConfigureAwait(false);
    }

    private async Task AcceptTcpLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient? publicClient = null;
            ulong connectionId = 0;

            try
            {
                var listener = _tcpListener;
                if (listener is null)
                {
                    return;
                }

                publicClient = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                publicClient.NoDelay = true;
                connectionId = NextConnectionId();
                if (!_tcpConnections.TryAdd(connectionId, publicClient))
                {
                    publicClient.Dispose();
                    continue;
                }

                if (!await SendToClientAsync(new PortTunnelRelayFrame(PortTunnelRelayFrameType.TcpOpen, connectionId, ReadOnlyMemory<byte>.Empty), cancellationToken).ConfigureAwait(false))
                {
                    CloseTcpConnection(connectionId);
                    continue;
                }

                _ = Task.Run(() => RelayTcpPublicToClientAsync(connectionId, publicClient, cancellationToken), CancellationToken.None);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (SocketException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                publicClient?.Dispose();
                if (connectionId != 0)
                {
                    _tcpConnections.TryRemove(connectionId, out _);
                }

                _logger.LogError(ex, "Remote TCP tunnel {TunnelName} accept loop failed.", _definition.Name);
                _runtimeCancellation?.Cancel();
                return;
            }
        }
    }

    private async Task RelayTcpPublicToClientAsync(ulong connectionId, TcpClient publicClient, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            using var stream = publicClient.GetStream();
            while (!cancellationToken.IsCancellationRequested)
            {
                var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    return;
                }

                if (!await _trafficLimiter.ConsumeAsync(bytesRead, cancellationToken).ConfigureAwait(false))
                {
                    await StopForTrafficLimitAsync(cancellationToken).ConfigureAwait(false);
                    return;
                }

                var payload = new byte[bytesRead];
                Buffer.BlockCopy(buffer, 0, payload, 0, bytesRead);
                if (!await SendToClientAsync(new PortTunnelRelayFrame(PortTunnelRelayFrameType.TcpData, connectionId, payload), cancellationToken).ConfigureAwait(false))
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (IOException)
        {
        }
        catch (SocketException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Remote TCP tunnel {TunnelName} public connection {ConnectionId} failed.", _definition.Name, connectionId);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            if (_tcpConnections.TryRemove(connectionId, out var client))
            {
                client.Dispose();
                await SendToClientAsync(new PortTunnelRelayFrame(PortTunnelRelayFrameType.TcpClose, connectionId, ReadOnlyMemory<byte>.Empty), CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private async Task ReceiveUdpPublicLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var udpSocket = _udpSocket;
                if (udpSocket is null)
                {
                    return;
                }

                var datagram = await udpSocket.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                CleanupIdleUdpSessions();

                if (!await _trafficLimiter.ConsumeAsync(datagram.Buffer.Length, cancellationToken).ConfigureAwait(false))
                {
                    await StopForTrafficLimitAsync(cancellationToken).ConfigureAwait(false);
                    return;
                }

                var session = GetOrCreateUdpSession(datagram.RemoteEndPoint);
                session.LastSeen = DateTimeOffset.UtcNow;
                if (!await SendToClientAsync(new PortTunnelRelayFrame(PortTunnelRelayFrameType.UdpDatagram, session.Id, datagram.Buffer), cancellationToken).ConfigureAwait(false))
                {
                    return;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (SocketException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Remote UDP tunnel {TunnelName} receive loop failed.", _definition.Name);
                _runtimeCancellation?.Cancel();
                return;
            }
        }
    }

    private async Task ReceiveClientLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        using var messageBuffer = new MemoryStream();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var socket = _webSocket;
                if (socket is null)
                {
                    return;
                }

                var frame = await PortTunnelRelayWebSocket.ReceiveAsync(socket, buffer, messageBuffer, cancellationToken).ConfigureAwait(false);
                if (frame is null)
                {
                    return;
                }

                await HandleClientFrameAsync(frame, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task HandleClientFrameAsync(PortTunnelRelayFrame frame, CancellationToken cancellationToken)
    {
        switch (frame.Type)
        {
            case PortTunnelRelayFrameType.TcpData:
                await WriteTcpPublicAsync(frame, cancellationToken).ConfigureAwait(false);
                break;
            case PortTunnelRelayFrameType.TcpClose:
                CloseTcpConnection(frame.ConnectionId);
                break;
            case PortTunnelRelayFrameType.UdpDatagram:
                await WriteUdpPublicAsync(frame, cancellationToken).ConfigureAwait(false);
                break;
            case PortTunnelRelayFrameType.Error:
                _logger.LogWarning("Remote tunnel client reported an error for {TunnelName}: {Error}", _definition.Name, frame.GetTextPayload());
                break;
            case PortTunnelRelayFrameType.Ping:
                break;
            default:
                _logger.LogWarning("Remote tunnel client sent unsupported frame type {FrameType}.", frame.Type);
                break;
        }
    }

    private async Task WriteTcpPublicAsync(PortTunnelRelayFrame frame, CancellationToken cancellationToken)
    {
        if (!_tcpConnections.TryGetValue(frame.ConnectionId, out var publicClient))
        {
            return;
        }

        if (!await _trafficLimiter.ConsumeAsync(frame.Payload.Length, cancellationToken).ConfigureAwait(false))
        {
            await StopForTrafficLimitAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            await publicClient.GetStream().WriteAsync(frame.Payload, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException)
        {
            CloseTcpConnection(frame.ConnectionId);
        }
    }

    private async Task WriteUdpPublicAsync(PortTunnelRelayFrame frame, CancellationToken cancellationToken)
    {
        var udpSocket = _udpSocket;
        if (udpSocket is null || !_udpSessionsById.TryGetValue(frame.ConnectionId, out var session))
        {
            return;
        }

        if (!await _trafficLimiter.ConsumeAsync(frame.Payload.Length, cancellationToken).ConfigureAwait(false))
        {
            await StopForTrafficLimitAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        session.LastSeen = DateTimeOffset.UtcNow;
        await udpSocket.SendAsync(frame.Payload.ToArray(), frame.Payload.Length, session.PublicPeer).ConfigureAwait(false);
    }

    private UdpPublicSession GetOrCreateUdpSession(IPEndPoint publicPeer)
    {
        var key = publicPeer.ToString();
        return _udpSessionsByPeer.GetOrAdd(key, _ =>
        {
            var session = new UdpPublicSession(NextConnectionId(), publicPeer);
            _udpSessionsById[session.Id] = session;
            return session;
        });
    }

    private void CleanupIdleUdpSessions()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var item in _udpSessionsByPeer.ToArray())
        {
            if (now - item.Value.LastSeen <= UdpSessionIdleTimeout)
            {
                continue;
            }

            if (_udpSessionsByPeer.TryRemove(item.Key, out var session))
            {
                _udpSessionsById.TryRemove(session.Id, out _);
            }
        }
    }

    private void CloseTcpConnection(ulong connectionId)
    {
        if (_tcpConnections.TryRemove(connectionId, out var publicClient))
        {
            publicClient.Dispose();
        }
    }

    private async Task StopForTrafficLimitAsync(CancellationToken cancellationToken)
    {
        await SendToClientAsync(PortTunnelRelayFrame.Text(PortTunnelRelayFrameType.Error, 0, "Traffic limit exceeded."), cancellationToken).ConfigureAwait(false);
        _logger.LogWarning("Remote tunnel {TunnelName} stopped because its traffic limit was exceeded.", _definition.Name);
        _runtimeCancellation?.Cancel();
    }

    private async Task<bool> SendToClientAsync(PortTunnelRelayFrame frame, CancellationToken cancellationToken)
    {
        var socket = _webSocket;
        if (socket is null || socket.State != WebSocketState.Open)
        {
            return false;
        }

        try
        {
            await PortTunnelRelayWebSocket.SendAsync(socket, frame, _sendLock, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException or IOException)
        {
            _logger.LogInformation(ex, "Remote tunnel {TunnelName} client connection closed.", _definition.Name);
            _runtimeCancellation?.Cancel();
            return false;
        }
    }

    private ulong NextConnectionId()
    {
        return (ulong)Interlocked.Increment(ref _nextConnectionId);
    }

    private sealed class UdpPublicSession
    {
        public UdpPublicSession(ulong id, IPEndPoint publicPeer)
        {
            Id = id;
            PublicPeer = publicPeer;
            LastSeen = DateTimeOffset.UtcNow;
        }

        public ulong Id { get; }

        public IPEndPoint PublicPeer { get; }

        public DateTimeOffset LastSeen { get; set; }
    }
}
