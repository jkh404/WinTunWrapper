using System.Collections.Concurrent;
using System.Net.WebSockets;
using ProxyNetworker.Server.Data;
using ProxyNetworker.Server.Management;

namespace ProxyNetworker.Server.PortTunnels;

public sealed class RemotePortTunnelRegistry : IDisposable
{
    private readonly ConcurrentDictionary<string, RemotePortTunnelRelay> _relays = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILoggerFactory _loggerFactory;

    public RemotePortTunnelRegistry(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public IReadOnlyCollection<TunnelSummary> List()
    {
        return _relays.Values
            .Select(item => item.ToSummary())
            .OrderBy(item => item.StartedAt)
            .ToArray();
    }

    public async Task RunClientAsync(PortTunnelDefinitionRecord definition, WebSocket webSocket, CancellationToken cancellationToken)
    {
        var relay = new RemotePortTunnelRelay(definition, _loggerFactory.CreateLogger<RemotePortTunnelRelay>());
        if (!_relays.TryAdd(definition.Id, relay))
        {
            throw new InvalidOperationException($"Port tunnel '{definition.Name}' already has a connected client.");
        }

        try
        {
            await relay.RunAsync(webSocket, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _relays.TryRemove(definition.Id, out _);
            relay.Dispose();
        }
    }

    public void Dispose()
    {
        foreach (var item in _relays.ToArray())
        {
            if (_relays.TryRemove(item.Key, out var relay))
            {
                relay.Dispose();
            }
        }
    }
}
