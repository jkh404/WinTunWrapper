using System.Collections.Concurrent;
using System.Net;
using ProxyNetworker.Core;
using ProxyNetworker.Server.Data;
using ProxyNetworker.Server.Management;

namespace ProxyNetworker.Server.VirtualNetworks;

public sealed class VirtualNetworkRuntimeRegistry : IDisposable
{
    private readonly ConcurrentDictionary<string, ActiveVirtualNetwork> _networks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<VirtualNetworkRuntimeRegistry> _logger;

    public VirtualNetworkRuntimeRegistry(ILogger<VirtualNetworkRuntimeRegistry> logger)
    {
        _logger = logger;
    }

    public IReadOnlyCollection<TunnelSummary> List()
    {
        return _networks.Values
            .Select(static item => item.Summary)
            .OrderBy(static item => item.StartedAt)
            .ToArray();
    }

    public async Task<TunnelSummary> EnsureRunningAsync(VirtualNetworkRecord definition, CancellationToken cancellationToken)
    {
        if (_networks.TryGetValue(definition.Id, out var existing))
        {
            return existing.Summary;
        }

        var gate = _locks.GetOrAdd(definition.Id, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_networks.TryGetValue(definition.Id, out existing))
            {
                return existing.Summary;
            }

            var runtime = await StartAsync(definition, cancellationToken).ConfigureAwait(false);
            if (!_networks.TryAdd(definition.Id, runtime))
            {
                runtime.Relay.Dispose();
                return _networks[definition.Id].Summary;
            }

            return runtime.Summary;
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose()
    {
        foreach (var item in _networks.ToArray())
        {
            if (_networks.TryRemove(item.Key, out var runtime))
            {
                runtime.Relay.Dispose();
            }
        }

        foreach (var item in _locks.ToArray())
        {
            if (_locks.TryRemove(item.Key, out var gate))
            {
                gate.Dispose();
            }
        }
    }

    private async Task<ActiveVirtualNetwork> StartAsync(VirtualNetworkRecord definition, CancellationToken cancellationToken)
    {
        var gatewayAddress = IPAddress.Parse(definition.GatewayAddress);
        var relay = new VirtualNetworkRelay(
            new VirtualNetworkTunnelOptions
            {
                BindEndPoint = new IPEndPoint(IPAddress.Any, definition.ListenPort),
                NodeAddress = gatewayAddress,
                PrefixLength = definition.PrefixLength,
                NodeName = definition.Name
            },
            LogCoreEntry);

        await relay.StartAsync(cancellationToken).ConfigureAwait(false);

        var summary = new TunnelSummary(
            definition.Id,
            definition.Name,
            "virtual-network",
            "udp",
            $"0.0.0.0:{definition.ListenPort}",
            null,
            DateTimeOffset.UtcNow,
            "running");

        _logger.LogInformation(
            "Virtual Network {NetworkName} relay is running on UDP {ListenPort} with gateway {GatewayAddress}/{PrefixLength}.",
            definition.Name,
            definition.ListenPort,
            definition.GatewayAddress,
            definition.PrefixLength);

        return new ActiveVirtualNetwork(summary, relay);
    }

    private void LogCoreEntry(ProxyNetworkerLogEntry entry)
    {
        var level = entry.Level switch
        {
            ProxyNetworkerLogLevel.Trace => LogLevel.Trace,
            ProxyNetworkerLogLevel.Debug => LogLevel.Debug,
            ProxyNetworkerLogLevel.Information => LogLevel.Information,
            ProxyNetworkerLogLevel.Warning => LogLevel.Warning,
            ProxyNetworkerLogLevel.Error => LogLevel.Error,
            _ => LogLevel.Information
        };

        _logger.Log(level, entry.Exception, "{Message}", entry.Message);
    }

    private sealed record ActiveVirtualNetwork(TunnelSummary Summary, VirtualNetworkRelay Relay);
}
