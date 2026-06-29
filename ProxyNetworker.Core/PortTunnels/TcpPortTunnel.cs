using System.Net.Sockets;

namespace ProxyNetworker.Core.PortTunnels;

internal sealed class TcpPortTunnel : IPortTunnel
{
    private readonly PortTunnelRule _rule;
    private readonly Action<ProxyNetworkerLogEntry>? _logger;
    private readonly List<TcpClient> _connections = new();
    private readonly object _connectionsLock = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _acceptCancellation;
    private Task? _acceptLoop;
    private int _started;

    public TcpPortTunnel(PortTunnelRule rule, Action<ProxyNetworkerLogEntry>? logger)
    {
        _rule = rule;
        _logger = logger;
    }

    public string Name => _rule.Name;

    public PortTunnelProtocol Protocol => PortTunnelProtocol.Tcp;

    public bool IsStarted => _started == 1;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            return Task.CompletedTask;
        }

        try
        {
            _listener = new TcpListener(_rule.PublicEndpoint);
            _listener.Start();
            _acceptCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _acceptLoop = Task.Run(() => AcceptLoopAsync(_acceptCancellation.Token));
            Log(ProxyNetworkerLogLevel.Information, $"TCP Port Tunnel '{Name}' listening on {_rule.PublicEndpoint}, private service {_rule.PrivateService}.");
            return Task.CompletedTask;
        }
        catch
        {
            Interlocked.Exchange(ref _started, 0);
            _listener?.Stop();
            _listener = null;
            _acceptCancellation?.Dispose();
            _acceptCancellation = null;
            throw;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _started, 0) == 1)
        {
            _acceptCancellation?.Cancel();
            _listener?.Stop();

            lock (_connectionsLock)
            {
                foreach (var connection in _connections)
                {
                    connection.Dispose();
                }

                _connections.Clear();
            }

            try
            {
                _acceptLoop?.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException ex) when (ex.InnerExceptions.All(static item => item is OperationCanceledException or ObjectDisposedException or SocketException))
            {
            }

            _acceptCancellation?.Dispose();
        }
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient? publicClient = null;

            try
            {
                var listener = _listener;
                if (listener is null)
                {
                    return;
                }

                publicClient = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                Track(publicClient);
                _ = HandleConnectionAsync(publicClient, cancellationToken);
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
                Log(ProxyNetworkerLogLevel.Error, $"TCP Port Tunnel '{Name}' accept loop failed.", ex);
                return;
            }
        }
    }

    private async Task HandleConnectionAsync(TcpClient publicClient, CancellationToken cancellationToken)
    {
        using (publicClient)
        {
            TcpClient? privateClient = null;

            try
            {
                privateClient = new TcpClient();
                var privateService = await _rule.PrivateService.ResolveAsync(cancellationToken).ConfigureAwait(false);
                await privateClient.ConnectAsync(privateService.Address, privateService.Port).ConfigureAwait(false);
                Track(privateClient);

                using (privateClient)
                {
                    using var publicStream = publicClient.GetStream();
                    using var privateStream = privateClient.GetStream();
                    var publicToPrivate = publicStream.CopyToAsync(privateStream, _rule.BufferSize, cancellationToken);
                    var privateToPublic = privateStream.CopyToAsync(publicStream, _rule.BufferSize, cancellationToken);
                    await Task.WhenAny(publicToPrivate, privateToPublic).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                Log(ProxyNetworkerLogLevel.Warning, $"TCP Port Tunnel '{Name}' connection failed.", ex);
            }
            finally
            {
                Untrack(publicClient);
                if (privateClient is not null)
                {
                    Untrack(privateClient);
                }
            }
        }
    }

    private void Track(TcpClient client)
    {
        lock (_connectionsLock)
        {
            _connections.Add(client);
        }
    }

    private void Untrack(TcpClient client)
    {
        lock (_connectionsLock)
        {
            _connections.Remove(client);
        }
    }

    private void Log(ProxyNetworkerLogLevel level, string message, Exception? exception = null)
    {
        _logger?.Invoke(new ProxyNetworkerLogEntry(level, message, exception));
    }
}
