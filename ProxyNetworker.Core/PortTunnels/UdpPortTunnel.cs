using System.Net;
using System.Net.Sockets;

namespace ProxyNetworker.Core.PortTunnels;

internal sealed class UdpPortTunnel : IPortTunnel
{
    private readonly PortTunnelRule _rule;
    private readonly Action<ProxyNetworkerLogEntry>? _logger;
    private readonly Dictionary<string, UdpSession> _sessions = new();
    private readonly object _sessionsLock = new();
    private UdpClient? _publicSocket;
    private IPEndPoint? _privateService;
    private CancellationTokenSource? _receiveCancellation;
    private Task? _receiveLoop;
    private int _started;

    public UdpPortTunnel(PortTunnelRule rule, Action<ProxyNetworkerLogEntry>? logger)
    {
        _rule = rule;
        _logger = logger;
    }

    public string Name => _rule.Name;

    public PortTunnelProtocol Protocol => PortTunnelProtocol.Udp;

    public bool IsStarted => _started == 1;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            return;
        }

        try
        {
            _privateService = await _rule.PrivateService.ResolveAsync(cancellationToken).ConfigureAwait(false);
            _publicSocket = new UdpClient(_rule.PublicEndpoint);
            _receiveCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _receiveLoop = Task.Run(() => ReceivePublicLoopAsync(_receiveCancellation.Token));
            Log(ProxyNetworkerLogLevel.Information, $"UDP Port Tunnel '{Name}' listening on {_rule.PublicEndpoint}, private service {_privateService}.");
        }
        catch
        {
            Interlocked.Exchange(ref _started, 0);
            _publicSocket?.Dispose();
            _publicSocket = null;
            _receiveCancellation?.Dispose();
            _receiveCancellation = null;
            throw;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _started, 0) == 1)
        {
            _receiveCancellation?.Cancel();
            _publicSocket?.Dispose();

            lock (_sessionsLock)
            {
                foreach (var session in _sessions.Values)
                {
                    session.Dispose();
                }

                _sessions.Clear();
            }

            try
            {
                _receiveLoop?.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException ex) when (ex.InnerExceptions.All(static item => item is OperationCanceledException or ObjectDisposedException or SocketException))
            {
            }

            _receiveCancellation?.Dispose();
        }
    }

    private async Task ReceivePublicLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var publicSocket = _publicSocket;
                var privateService = _privateService;
                if (publicSocket is null || privateService is null)
                {
                    return;
                }

                var datagram = await publicSocket.ReceiveAsync().ConfigureAwait(false);
                CleanupIdleSessions();
                var session = GetOrCreateSession(datagram.RemoteEndPoint, privateService, cancellationToken);
                session.LastSeen = DateTimeOffset.UtcNow;
                await session.PrivateSocket.SendAsync(datagram.Buffer, datagram.Buffer.Length, privateService).ConfigureAwait(false);
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
                Log(ProxyNetworkerLogLevel.Error, $"UDP Port Tunnel '{Name}' receive loop failed.", ex);
                return;
            }
        }
    }

    private UdpSession GetOrCreateSession(IPEndPoint publicPeer, IPEndPoint privateService, CancellationToken cancellationToken)
    {
        var key = publicPeer.ToString();

        lock (_sessionsLock)
        {
            if (_sessions.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var session = new UdpSession(publicPeer, new UdpClient(publicPeer.AddressFamily));
            session.ReceiveLoop = Task.Run(() => ReceivePrivateLoopAsync(session, cancellationToken));
            _sessions.Add(key, session);
            Log(ProxyNetworkerLogLevel.Debug, $"UDP Port Tunnel '{Name}' created session {publicPeer} -> {privateService}.");
            return session;
        }
    }

    private async Task ReceivePrivateLoopAsync(UdpSession session, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var publicSocket = _publicSocket;
                if (publicSocket is null)
                {
                    return;
                }

                var datagram = await session.PrivateSocket.ReceiveAsync().ConfigureAwait(false);
                session.LastSeen = DateTimeOffset.UtcNow;
                await publicSocket.SendAsync(datagram.Buffer, datagram.Buffer.Length, session.PublicPeer).ConfigureAwait(false);
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
                Log(ProxyNetworkerLogLevel.Warning, $"UDP Port Tunnel '{Name}' session failed for {session.PublicPeer}.", ex);
                RemoveSession(session.PublicPeer);
                return;
            }
        }
    }

    private void CleanupIdleSessions()
    {
        var now = DateTimeOffset.UtcNow;
        var expired = new List<string>();

        lock (_sessionsLock)
        {
            foreach (var item in _sessions)
            {
                if (now - item.Value.LastSeen > _rule.UdpSessionIdleTimeout)
                {
                    expired.Add(item.Key);
                }
            }

            foreach (var key in expired)
            {
                _sessions[key].Dispose();
                _sessions.Remove(key);
            }
        }
    }

    private void RemoveSession(IPEndPoint publicPeer)
    {
        var key = publicPeer.ToString();

        lock (_sessionsLock)
        {
            if (_sessions.TryGetValue(key, out var session))
            {
                session.Dispose();
                _sessions.Remove(key);
            }
        }
    }

    private void Log(ProxyNetworkerLogLevel level, string message, Exception? exception = null)
    {
        _logger?.Invoke(new ProxyNetworkerLogEntry(level, message, exception));
    }

    private sealed class UdpSession : IDisposable
    {
        public UdpSession(IPEndPoint publicPeer, UdpClient privateSocket)
        {
            PublicPeer = publicPeer;
            PrivateSocket = privateSocket;
            LastSeen = DateTimeOffset.UtcNow;
        }

        public IPEndPoint PublicPeer { get; }

        public UdpClient PrivateSocket { get; }

        public DateTimeOffset LastSeen { get; set; }

        public Task? ReceiveLoop { get; set; }

        public void Dispose()
        {
            PrivateSocket.Dispose();
        }
    }
}
