using System.Net;
using System.Net.Sockets;

namespace ProxyNetworker.Core;

public sealed class VirtualNetworkRelay : IDisposable
{
    private readonly VirtualNetworkTunnelOptions _options;
    private readonly Action<ProxyNetworkerLogEntry>? _logger;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly object _peersLock = new();
    private readonly Dictionary<string, VirtualNetworkPeer> _peersByAddress = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _addressByEndpoint = new(StringComparer.OrdinalIgnoreCase);
    private UdpClient? _udpClient;
    private CancellationTokenSource? _runtimeCancellation;
    private Task? _receiveLoop;
    private Task? _cleanupLoop;
    private int _started;

    public VirtualNetworkRelay(
        VirtualNetworkTunnelOptions options,
        Action<ProxyNetworkerLogEntry>? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
        _options.RemoteEndPoint = null;
        _options.Validate();
    }

    public int PeerCount
    {
        get
        {
            lock (_peersLock)
            {
                return _peersByAddress.Count;
            }
        }
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            return Task.CompletedTask;
        }

        try
        {
            _udpClient = new UdpClient(_options.BindEndPoint);
            _runtimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _receiveLoop = Task.Run(() => ReceiveLoopAsync(_runtimeCancellation.Token));
            _cleanupLoop = Task.Run(() => CleanupLoopAsync(_runtimeCancellation.Token));

            Log(ProxyNetworkerLogLevel.Information, $"Virtual Network relay listening on UDP {_options.BindEndPoint} as gateway {_options.NodeAddress}/{_options.PrefixLength}.");
            return Task.CompletedTask;
        }
        catch
        {
            Interlocked.Exchange(ref _started, 0);
            _udpClient?.Dispose();
            _udpClient = null;
            _runtimeCancellation?.Dispose();
            _runtimeCancellation = null;
            throw;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _started, 0) == 1)
        {
            _runtimeCancellation?.Cancel();
            _udpClient?.Dispose();

            try
            {
                Task.WaitAll(new[] { _receiveLoop, _cleanupLoop }.Where(static item => item is not null).Cast<Task>().ToArray(), TimeSpan.FromSeconds(2));
            }
            catch (AggregateException ex) when (ex.InnerExceptions.All(static item => item is OperationCanceledException or ObjectDisposedException or SocketException))
            {
            }

            _runtimeCancellation?.Dispose();
        }

        _sendLock.Dispose();
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

                if (!VirtualNetworkFrame.TryParse(received.Buffer, out var frame))
                {
                    Log(ProxyNetworkerLogLevel.Warning, $"Ignored malformed Virtual Network datagram from {received.RemoteEndPoint}.");
                    continue;
                }

                await HandleFrameAsync(frame, received.RemoteEndPoint, cancellationToken).ConfigureAwait(false);
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
                Log(ProxyNetworkerLogLevel.Error, "Virtual Network relay receive loop failed.", ex);
                return;
            }
        }
    }

    private async Task CleanupLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.HeartbeatInterval, cancellationToken).ConfigureAwait(false);
                CleanupExpiredPeers();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Log(ProxyNetworkerLogLevel.Warning, "Virtual Network relay cleanup failed.", ex);
            }
        }
    }

    private async Task HandleFrameAsync(VirtualNetworkFrame frame, IPEndPoint remoteEndPoint, CancellationToken cancellationToken)
    {
        switch (frame.Type)
        {
            case VirtualNetworkFrameType.Hello:
            case VirtualNetworkFrameType.Heartbeat:
                RegisterPeer(frame.VirtualAddress, remoteEndPoint);
                break;

            case VirtualNetworkFrameType.Packet:
                var sourceAddress = TryReadIPv4Source(frame.Payload.Span, out var parsedSource)
                    ? parsedSource
                    : frame.VirtualAddress;
                RegisterPeer(sourceAddress, remoteEndPoint);
                await RoutePacketAsync(frame.Payload, remoteEndPoint, cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    private async Task RoutePacketAsync(ReadOnlyMemory<byte> packet, IPEndPoint sourcePeer, CancellationToken cancellationToken)
    {
        if (!TryReadIPv4Destination(packet.Span, out var destination))
        {
            return;
        }

        if (IsBroadcastOrMulticast(destination))
        {
            await SendPacketToPeersAsync(packet, exceptPeer: sourcePeer, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (destination.Equals(_options.NodeAddress))
        {
            return;
        }

        var peer = FindPeer(destination);
        if (peer is not null && !IsSamePeer(peer.EndPoint, sourcePeer))
        {
            await SendPacketFrameAsync(packet, peer.EndPoint, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task SendPacketToPeersAsync(ReadOnlyMemory<byte> packet, IPEndPoint exceptPeer, CancellationToken cancellationToken)
    {
        VirtualNetworkPeer[] peers;
        lock (_peersLock)
        {
            peers = _peersByAddress.Values.ToArray();
        }

        foreach (var peer in peers)
        {
            if (IsSamePeer(peer.EndPoint, exceptPeer))
            {
                continue;
            }

            await SendPacketFrameAsync(packet, peer.EndPoint, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task SendPacketFrameAsync(ReadOnlyMemory<byte> packet, IPEndPoint remoteEndPoint, CancellationToken cancellationToken)
    {
        var frame = VirtualNetworkFrame.Create(VirtualNetworkFrameType.Packet, _options.NodeAddress, packet.Span);
        await SendFrameAsync(frame, remoteEndPoint, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendFrameAsync(byte[] frame, IPEndPoint remoteEndPoint, CancellationToken cancellationToken)
    {
        var udpClient = _udpClient;
        if (udpClient is null)
        {
            return;
        }

        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await udpClient.SendAsync(frame, frame.Length, remoteEndPoint).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private void RegisterPeer(IPAddress virtualAddress, IPEndPoint remoteEndPoint)
    {
        if (virtualAddress.Equals(_options.NodeAddress))
        {
            return;
        }

        var addressKey = AddressKey(virtualAddress);
        var endpointKey = EndPointKey(remoteEndPoint);

        lock (_peersLock)
        {
            if (_addressByEndpoint.TryGetValue(endpointKey, out var oldAddressKey) && oldAddressKey != addressKey)
            {
                _peersByAddress.Remove(oldAddressKey);
            }

            _addressByEndpoint[endpointKey] = addressKey;

            if (_peersByAddress.TryGetValue(addressKey, out var peer))
            {
                peer.EndPoint = remoteEndPoint;
                peer.LastSeen = DateTimeOffset.UtcNow;
                return;
            }

            _peersByAddress[addressKey] = new VirtualNetworkPeer(virtualAddress, remoteEndPoint);
            Log(ProxyNetworkerLogLevel.Information, $"Virtual Network peer {virtualAddress} registered from {remoteEndPoint}.");
        }
    }

    private VirtualNetworkPeer? FindPeer(IPAddress virtualAddress)
    {
        lock (_peersLock)
        {
            return _peersByAddress.TryGetValue(AddressKey(virtualAddress), out var peer) ? peer : null;
        }
    }

    private void CleanupExpiredPeers()
    {
        var expiresBefore = DateTimeOffset.UtcNow - _options.PeerTimeout;
        var expired = new List<string>();

        lock (_peersLock)
        {
            foreach (var item in _peersByAddress)
            {
                if (item.Value.LastSeen < expiresBefore)
                {
                    expired.Add(item.Key);
                }
            }

            foreach (var key in expired)
            {
                var peer = _peersByAddress[key];
                _addressByEndpoint.Remove(EndPointKey(peer.EndPoint));
                _peersByAddress.Remove(key);
                Log(ProxyNetworkerLogLevel.Information, $"Virtual Network peer {peer.VirtualAddress} expired.");
            }
        }
    }

    private bool IsBroadcastOrMulticast(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes[0] >= 224 || address.Equals(IPAddress.Broadcast) || address.Equals(GetSubnetBroadcastAddress());
    }

    private IPAddress GetSubnetBroadcastAddress()
    {
        var address = IPv4ToUInt32(_options.NodeAddress);
        var mask = _options.PrefixLength == 0 ? 0u : uint.MaxValue << (32 - _options.PrefixLength);
        return UInt32ToIPv4(address | ~mask);
    }

    private static bool TryReadIPv4Source(ReadOnlySpan<byte> packet, out IPAddress address)
    {
        return TryReadIPv4Address(packet, 12, out address);
    }

    private static bool TryReadIPv4Destination(ReadOnlySpan<byte> packet, out IPAddress address)
    {
        return TryReadIPv4Address(packet, 16, out address);
    }

    private static bool TryReadIPv4Address(ReadOnlySpan<byte> packet, int offset, out IPAddress address)
    {
        address = IPAddress.None;
        if (packet.Length < 20 || (packet[0] >> 4) != 4 || packet.Length < offset + 4)
        {
            return false;
        }

        address = new IPAddress(new[] { packet[offset], packet[offset + 1], packet[offset + 2], packet[offset + 3] });
        return true;
    }

    private static uint IPv4ToUInt32(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }

    private static IPAddress UInt32ToIPv4(uint value)
    {
        return new IPAddress(new[]
        {
            (byte)(value >> 24),
            (byte)(value >> 16),
            (byte)(value >> 8),
            (byte)value
        });
    }

    private static bool IsSamePeer(IPEndPoint left, IPEndPoint right)
    {
        return EndPointEquals(left, right);
    }

    private static bool EndPointEquals(IPEndPoint left, IPEndPoint right)
    {
        return left.Port == right.Port && left.Address.Equals(right.Address);
    }

    private static string AddressKey(IPAddress address)
    {
        return address.ToString();
    }

    private static string EndPointKey(IPEndPoint endPoint)
    {
        return endPoint.ToString();
    }

    private void Log(ProxyNetworkerLogLevel level, string message, Exception? exception = null)
    {
        _logger?.Invoke(new ProxyNetworkerLogEntry(level, message, exception));
    }

    private sealed class VirtualNetworkPeer
    {
        public VirtualNetworkPeer(IPAddress virtualAddress, IPEndPoint endPoint)
        {
            VirtualAddress = virtualAddress;
            EndPoint = endPoint;
            LastSeen = DateTimeOffset.UtcNow;
        }

        public IPAddress VirtualAddress { get; }

        public IPEndPoint EndPoint { get; set; }

        public DateTimeOffset LastSeen { get; set; }
    }
}
