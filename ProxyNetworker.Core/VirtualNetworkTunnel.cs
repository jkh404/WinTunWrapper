using System.Net;
using System.Net.Sockets;

namespace ProxyNetworker.Core;

public sealed class VirtualNetworkTunnel : IDisposable
{
    private readonly ITunDevice _tunDevice;
    private readonly VirtualNetworkTunnelOptions _options;
    private readonly Action<ProxyNetworkerLogEntry>? _logger;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly object _remoteLock = new();
    private UdpClient? _udpClient;
    private IPEndPoint? _remoteEndPoint;
    private CancellationTokenSource? _receiveCancellation;
    private Task? _receiveLoop;
    private int _started;

    public VirtualNetworkTunnel(
        ITunDevice tunDevice,
        VirtualNetworkTunnelOptions options,
        Action<ProxyNetworkerLogEntry>? logger = null)
    {
        _tunDevice = tunDevice ?? throw new ArgumentNullException(nameof(tunDevice));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
        _options.Validate();
        _remoteEndPoint = _options.RemoteEndPoint;
    }

    public IPEndPoint? RemoteEndPoint
    {
        get
        {
            lock (_remoteLock)
            {
                return _remoteEndPoint;
            }
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            return;
        }

        try
        {
            _udpClient = new UdpClient(_options.BindEndPoint);
            _receiveCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _tunDevice.PacketReceived += OnTunPacketReceived;

            await _tunDevice.StartAsync(cancellationToken).ConfigureAwait(false);

            _receiveLoop = Task.Run(() => ReceiveLoopAsync(_receiveCancellation.Token));
            Log(ProxyNetworkerLogLevel.Information, $"Virtual Network listening on UDP {_options.BindEndPoint}.");
        }
        catch
        {
            Interlocked.Exchange(ref _started, 0);
            _tunDevice.PacketReceived -= OnTunPacketReceived;
            _udpClient?.Dispose();
            _udpClient = null;
            _receiveCancellation?.Dispose();
            _receiveCancellation = null;
            throw;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _started, 0) == 1)
        {
            _tunDevice.PacketReceived -= OnTunPacketReceived;
            _receiveCancellation?.Cancel();
            _udpClient?.Dispose();

            try
            {
                _receiveLoop?.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException ex) when (ex.InnerExceptions.All(static item => item is OperationCanceledException or ObjectDisposedException or SocketException))
            {
            }

            _receiveCancellation?.Dispose();
        }

        _sendLock.Dispose();
        _tunDevice.Dispose();
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var udpClient = _udpClient;
                if (udpClient is null)
                {
                    return;
                }

                var received = await udpClient.ReceiveAsync().ConfigureAwait(false);
                if (received.Buffer.Length == 0 || received.Buffer.Length > _options.MaxPacketSize)
                {
                    continue;
                }

                if (!ShouldAcceptRemote(received.RemoteEndPoint))
                {
                    Log(ProxyNetworkerLogLevel.Warning, $"Ignored packet from unexpected UDP peer {received.RemoteEndPoint}.");
                    continue;
                }

                LearnRemote(received.RemoteEndPoint);
                await _tunDevice.SendAsync(received.Buffer, cancellationToken).ConfigureAwait(false);
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
                Log(ProxyNetworkerLogLevel.Error, "UDP receive loop failed.", ex);
                return;
            }
        }
    }

    private void OnTunPacketReceived(object? sender, TunPacketReceivedEventArgs e)
    {
        var remoteEndPoint = RemoteEndPoint;
        if (remoteEndPoint is null)
        {
            return;
        }

        _ = SendToRemoteAsync(e.Packet, remoteEndPoint);
    }

    private async Task SendToRemoteAsync(byte[] packet, IPEndPoint remoteEndPoint)
    {
        try
        {
            var udpClient = _udpClient;
            if (udpClient is null || packet.Length == 0)
            {
                return;
            }

            await _sendLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await udpClient.SendAsync(packet, packet.Length, remoteEndPoint).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            Log(ProxyNetworkerLogLevel.Warning, "Failed to send TUN packet to UDP peer.", ex);
        }
    }

    private bool ShouldAcceptRemote(IPEndPoint remoteEndPoint)
    {
        var configuredRemote = _options.RemoteEndPoint;
        if (configuredRemote is not null)
        {
            return EndPointEquals(configuredRemote, remoteEndPoint);
        }

        lock (_remoteLock)
        {
            return _remoteEndPoint is null || EndPointEquals(_remoteEndPoint, remoteEndPoint);
        }
    }

    private void LearnRemote(IPEndPoint remoteEndPoint)
    {
        if (_options.RemoteEndPoint is not null)
        {
            return;
        }

        lock (_remoteLock)
        {
            if (_remoteEndPoint is null)
            {
                _remoteEndPoint = remoteEndPoint;
                Log(ProxyNetworkerLogLevel.Information, $"Learned UDP peer {remoteEndPoint}.");
            }
        }
    }

    private static bool EndPointEquals(IPEndPoint left, IPEndPoint right)
    {
        return left.Port == right.Port && left.Address.Equals(right.Address);
    }

    private void Log(ProxyNetworkerLogLevel level, string message, Exception? exception = null)
    {
        _logger?.Invoke(new ProxyNetworkerLogEntry(level, message, exception));
    }
}
