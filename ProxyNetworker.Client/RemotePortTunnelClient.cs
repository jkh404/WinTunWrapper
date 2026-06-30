using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.Json;
using ProxyNetworker.Core;
using ProxyNetworker.Core.PortTunnels;
using ProxyNetworker.Core.PortTunnels.Remote;

internal sealed class RemotePortTunnelClient : IDisposable
{
    private const int BufferSize = 64 * 1024;
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(2);

    private readonly Uri _endpoint;
    private readonly string _token;
    private readonly Action<ProxyNetworkerLogEntry>? _logger;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly ConcurrentDictionary<ulong, TcpClient> _tcpConnections = new();
    private readonly ConcurrentDictionary<ulong, UdpPrivateSession> _udpSessions = new();
    private ClientWebSocket? _webSocket;
    private RemotePortTunnelConfig? _config;
    private CancellationTokenSource? _connectionCancellation;
    private int _disposed;

    public RemotePortTunnelClient(Uri endpoint, string token, Action<ProxyNetworkerLogEntry>? logger = null)
    {
        _endpoint = endpoint;
        _token = string.IsNullOrWhiteSpace(token)
            ? throw new ArgumentException("Token cannot be empty.", nameof(token))
            : token.Trim();
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Log(ProxyNetworkerLogLevel.Warning, $"Remote port tunnel connection failed. retrying in {ReconnectDelay.TotalSeconds:0}s.", ex);
            }

            try
            {
                await Task.Delay(ReconnectDelay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        try
        {
            _connectionCancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        DisposeActiveConnections();
        _webSocket?.Dispose();
        _connectionCancellation?.Dispose();
        _sendLock.Dispose();
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        using var webSocket = new ClientWebSocket();
        webSocket.Options.SetRequestHeader("X-ProxyNetworker-Token", _token);

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _connectionCancellation = linkedCancellation;
        _webSocket = webSocket;

        try
        {
            Log(ProxyNetworkerLogLevel.Information, $"Connecting remote port tunnel endpoint {_endpoint}.");
            await webSocket.ConnectAsync(_endpoint, cancellationToken).ConfigureAwait(false);
            await ReceiveReadyFrameAsync(linkedCancellation.Token).ConfigureAwait(false);
            await ReceiveServerLoopAsync(linkedCancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            DisposeActiveConnections();
            if (ReferenceEquals(_connectionCancellation, linkedCancellation))
            {
                _connectionCancellation = null;
            }

            if (ReferenceEquals(_webSocket, webSocket))
            {
                _webSocket = null;
            }
        }
    }

    private async Task ReceiveReadyFrameAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[BufferSize];
        using var messageBuffer = new MemoryStream();
        var socket = _webSocket ?? throw new InvalidOperationException("WebSocket is not connected.");
        var frame = await PortTunnelRelayWebSocket.ReceiveAsync(socket, buffer, messageBuffer, cancellationToken).ConfigureAwait(false)
            ?? throw new WebSocketException("Server closed the tunnel before sending configuration.");

        if (frame.Type != PortTunnelRelayFrameType.Ready)
        {
            throw new InvalidDataException($"Expected Ready frame, got {frame.Type}.");
        }

        _config = JsonSerializer.Deserialize<RemotePortTunnelConfig>(frame.GetTextPayload())
            ?? throw new InvalidDataException("Server tunnel configuration is invalid.");

        Log(
            ProxyNetworkerLogLevel.Information,
            $"Remote port tunnel ready. name={_config.Name}, protocol={_config.Protocol}, public=:{_config.PublicPort}, private={_config.PrivateHost}:{_config.PrivatePort}.");
    }

    private async Task ReceiveServerLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[BufferSize];
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
                    Log(ProxyNetworkerLogLevel.Information, "Remote port tunnel server closed the connection.");
                    return;
                }

                await HandleServerFrameAsync(frame, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            DisposeActiveConnections();
        }
    }

    private async Task HandleServerFrameAsync(PortTunnelRelayFrame frame, CancellationToken cancellationToken)
    {
        switch (frame.Type)
        {
            case PortTunnelRelayFrameType.TcpOpen:
                await OpenTcpPrivateConnectionAsync(frame.ConnectionId, cancellationToken).ConfigureAwait(false);
                break;
            case PortTunnelRelayFrameType.TcpData:
                await WriteTcpPrivateAsync(frame, cancellationToken).ConfigureAwait(false);
                break;
            case PortTunnelRelayFrameType.TcpClose:
                CloseTcpPrivateConnection(frame.ConnectionId);
                break;
            case PortTunnelRelayFrameType.UdpDatagram:
                await WriteUdpPrivateAsync(frame, cancellationToken).ConfigureAwait(false);
                break;
            case PortTunnelRelayFrameType.Error:
                Log(ProxyNetworkerLogLevel.Warning, $"Remote port tunnel server reported an error: {frame.GetTextPayload()}");
                break;
            case PortTunnelRelayFrameType.Ping:
                await SendToServerAsync(new PortTunnelRelayFrame(PortTunnelRelayFrameType.Ping, 0, ReadOnlyMemory<byte>.Empty), cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    private async Task OpenTcpPrivateConnectionAsync(ulong connectionId, CancellationToken cancellationToken)
    {
        var config = RequireConfig();
        var tcpClient = new TcpClient
        {
            NoDelay = true
        };

        try
        {
            await tcpClient.ConnectAsync(config.PrivateHost, config.PrivatePort, cancellationToken).ConfigureAwait(false);
            if (!_tcpConnections.TryAdd(connectionId, tcpClient))
            {
                tcpClient.Dispose();
                return;
            }

            _ = Task.Run(() => RelayTcpPrivateToServerAsync(connectionId, tcpClient, cancellationToken), CancellationToken.None);
        }
        catch (Exception ex) when (ex is SocketException or IOException or ObjectDisposedException)
        {
            tcpClient.Dispose();
            Log(ProxyNetworkerLogLevel.Warning, $"Failed to connect private TCP service for connection {connectionId}.", ex);
            await SendToServerAsync(PortTunnelRelayFrame.Text(PortTunnelRelayFrameType.Error, connectionId, ex.Message), CancellationToken.None).ConfigureAwait(false);
            await SendToServerAsync(new PortTunnelRelayFrame(PortTunnelRelayFrameType.TcpClose, connectionId, ReadOnlyMemory<byte>.Empty), CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task RelayTcpPrivateToServerAsync(ulong connectionId, TcpClient tcpClient, CancellationToken cancellationToken)
    {
        var buffer = new byte[BufferSize];
        try
        {
            using var stream = tcpClient.GetStream();
            while (!cancellationToken.IsCancellationRequested)
            {
                var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    return;
                }

                var payload = new byte[bytesRead];
                Buffer.BlockCopy(buffer, 0, payload, 0, bytesRead);
                if (!await SendToServerAsync(new PortTunnelRelayFrame(PortTunnelRelayFrameType.TcpData, connectionId, payload), cancellationToken).ConfigureAwait(false))
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException)
        {
        }
        finally
        {
            if (_tcpConnections.TryRemove(connectionId, out var removed))
            {
                removed.Dispose();
                await SendToServerAsync(new PortTunnelRelayFrame(PortTunnelRelayFrameType.TcpClose, connectionId, ReadOnlyMemory<byte>.Empty), CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private async Task WriteTcpPrivateAsync(PortTunnelRelayFrame frame, CancellationToken cancellationToken)
    {
        if (!_tcpConnections.TryGetValue(frame.ConnectionId, out var tcpClient))
        {
            return;
        }

        try
        {
            await tcpClient.GetStream().WriteAsync(frame.Payload, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException)
        {
            CloseTcpPrivateConnection(frame.ConnectionId);
        }
    }

    private async Task WriteUdpPrivateAsync(PortTunnelRelayFrame frame, CancellationToken cancellationToken)
    {
        var session = await GetOrCreateUdpPrivateSessionAsync(frame.ConnectionId, cancellationToken).ConfigureAwait(false);
        session.LastSeen = DateTimeOffset.UtcNow;
        await session.Socket.SendAsync(frame.Payload.ToArray(), frame.Payload.Length).ConfigureAwait(false);
    }

    private async Task<UdpPrivateSession> GetOrCreateUdpPrivateSessionAsync(ulong connectionId, CancellationToken cancellationToken)
    {
        if (_udpSessions.TryGetValue(connectionId, out var existing))
        {
            return existing;
        }

        var config = RequireConfig();
        var target = await new HostEndPoint(config.PrivateHost, config.PrivatePort).ResolveAsync(cancellationToken).ConfigureAwait(false);
        var udpClient = new UdpClient(target.AddressFamily);
        udpClient.Connect(target);
        var session = new UdpPrivateSession(connectionId, udpClient);
        if (!_udpSessions.TryAdd(connectionId, session))
        {
            udpClient.Dispose();
            return _udpSessions[connectionId];
        }

        session.ReceiveLoop = Task.Run(() => RelayUdpPrivateToServerAsync(session, cancellationToken), CancellationToken.None);
        return session;
    }

    private async Task RelayUdpPrivateToServerAsync(UdpPrivateSession session, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var datagram = await session.Socket.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                session.LastSeen = DateTimeOffset.UtcNow;
                if (!await SendToServerAsync(new PortTunnelRelayFrame(PortTunnelRelayFrameType.UdpDatagram, session.ConnectionId, datagram.Buffer), cancellationToken).ConfigureAwait(false))
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is ObjectDisposedException or SocketException)
        {
        }
        finally
        {
            if (_udpSessions.TryRemove(session.ConnectionId, out var removed))
            {
                removed.Dispose();
            }
        }
    }

    private void CloseTcpPrivateConnection(ulong connectionId)
    {
        if (_tcpConnections.TryRemove(connectionId, out var tcpClient))
        {
            tcpClient.Dispose();
        }
    }

    private void DisposeActiveConnections()
    {
        foreach (var item in _tcpConnections.ToArray())
        {
            if (_tcpConnections.TryRemove(item.Key, out var tcpClient))
            {
                tcpClient.Dispose();
            }
        }

        foreach (var item in _udpSessions.ToArray())
        {
            if (_udpSessions.TryRemove(item.Key, out var udpSession))
            {
                udpSession.Dispose();
            }
        }
    }

    private async Task<bool> SendToServerAsync(PortTunnelRelayFrame frame, CancellationToken cancellationToken)
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
            _connectionCancellation?.Cancel();
            return false;
        }
    }

    private RemotePortTunnelConfig RequireConfig()
    {
        return _config ?? throw new InvalidOperationException("Remote port tunnel configuration has not been received.");
    }

    private void Log(ProxyNetworkerLogLevel level, string message, Exception? exception = null)
    {
        _logger?.Invoke(new ProxyNetworkerLogEntry(level, message, exception));
    }

    private sealed class UdpPrivateSession : IDisposable
    {
        public UdpPrivateSession(ulong connectionId, UdpClient socket)
        {
            ConnectionId = connectionId;
            Socket = socket;
            LastSeen = DateTimeOffset.UtcNow;
        }

        public ulong ConnectionId { get; }

        public UdpClient Socket { get; }

        public DateTimeOffset LastSeen { get; set; }

        public Task? ReceiveLoop { get; set; }

        public void Dispose()
        {
            Socket.Dispose();
        }
    }
}
