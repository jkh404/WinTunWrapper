using System.Collections.Concurrent;
using System.Net;
using Microsoft.EntityFrameworkCore;
using ProxyNetworker.Core;
using ProxyNetworker.Core.PortTunnels;
using ProxyNetworker.Server.Data;

namespace ProxyNetworker.Server.Management;

public sealed class TunnelRegistry : IDisposable
{
    private readonly ConcurrentDictionary<string, ActiveTunnel> _tunnels = new(StringComparer.OrdinalIgnoreCase);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TunnelRegistry> _logger;

    public TunnelRegistry(IServiceScopeFactory scopeFactory, ILogger<TunnelRegistry> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<TunnelSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProxyNetworkerDbContext>();

        var records = await db.Tunnels
            .AsNoTracking()
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        return records
            .OrderBy(static item => item.StartedAt)
            .Select(static item => new TunnelSummary(
                item.Id,
                item.Name,
                item.Kind,
                item.Protocol,
                item.PublicEndpoint,
                item.PrivateService,
                item.StartedAt,
                item.Status))
            .ToArray();
    }

    public async Task<TunnelSummary> StartPortTunnelAsync(CreatePortTunnelRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var protocol = ParseProtocol(request.Protocol);
        var publicEndpoint = ParsePublicEndpoint(request.ListenAddress, request.ListenPort);
        var privateService = new HostEndPoint(request.TargetHost, request.TargetPort);
        var name = NormalizeName(request.Name, "port-tunnel");

        var tunnel = PortTunnelFactory.Create(new PortTunnelRule
        {
            Name = name,
            Protocol = protocol,
            PublicEndpoint = publicEndpoint,
            PrivateService = privateService
        }, LogCoreEntry);

        await tunnel.StartAsync(cancellationToken).ConfigureAwait(false);

        var summary = new TunnelSummary(
            Id: NewId(),
            Name: name,
            Kind: "port-tunnel",
            Protocol: protocol.ToString().ToLowerInvariant(),
            PublicEndpoint: publicEndpoint.ToString(),
            PrivateService: privateService.ToString(),
            StartedAt: DateTimeOffset.UtcNow,
            Status: "running");

        await AddAsync(summary, tunnel, cancellationToken).ConfigureAwait(false);
        return summary;
    }

    public async Task<TunnelSummary> StartVirtualNetworkAsync(CreateVirtualNetworkRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var tunAddress = IPAddress.Parse(request.TunAddress);
        var listen = new IPEndPoint(IPAddress.Any, ValidatePort(request.ListenPort, nameof(request.ListenPort)));
        var name = NormalizeName(request.Name, "virtual-network");

        var tunDevice = TunDeviceFactory.Create(new TunDeviceOptions
        {
            Name = name,
            TunnelType = "ProxyNetworker",
            Address = tunAddress,
            PrefixLength = request.PrefixLength,
            Mtu = request.Mtu
        }, LogCoreEntry);

        VirtualNetworkTunnel? tunnel = null;

        try
        {
            tunnel = new VirtualNetworkTunnel(
                tunDevice,
                new VirtualNetworkTunnelOptions
                {
                    BindEndPoint = listen,
                    NodeAddress = tunAddress,
                    PrefixLength = request.PrefixLength,
                    NodeName = name
                },
                LogCoreEntry);

            await tunnel.StartAsync(cancellationToken).ConfigureAwait(false);

            var summary = new TunnelSummary(
                Id: NewId(),
                Name: name,
                Kind: "virtual-network",
                Protocol: "udp",
                PublicEndpoint: listen.ToString(),
                PrivateService: null,
                StartedAt: DateTimeOffset.UtcNow,
                Status: "running");

            await AddAsync(summary, tunnel, cancellationToken).ConfigureAwait(false);
            return summary;
        }
        catch
        {
            tunnel?.Dispose();
            tunDevice.Dispose();
            throw;
        }
    }

    public async Task<bool> StopAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Tunnel id cannot be empty.", nameof(id));
        }

        if (!_tunnels.TryRemove(id, out var activeTunnel))
        {
            return false;
        }

        activeTunnel.Runtime.Dispose();
        await MarkStoppedAsync(id, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public void Dispose()
    {
        foreach (var item in _tunnels.ToArray())
        {
            if (_tunnels.TryRemove(item.Key, out var activeTunnel))
            {
                activeTunnel.Runtime.Dispose();
            }
        }
    }

    private async Task AddAsync(TunnelSummary summary, IDisposable runtime, CancellationToken cancellationToken)
    {
        if (!_tunnels.TryAdd(summary.Id, new ActiveTunnel(summary, runtime)))
        {
            runtime.Dispose();
            throw new InvalidOperationException($"Tunnel id collision: {summary.Id}.");
        }

        try
        {
            await InsertRecordAsync(summary, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (_tunnels.TryRemove(summary.Id, out var activeTunnel))
            {
                activeTunnel.Runtime.Dispose();
            }

            throw;
        }
    }

    private async Task InsertRecordAsync(TunnelSummary summary, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProxyNetworkerDbContext>();
        db.Tunnels.Add(new TunnelRecord
        {
            Id = summary.Id,
            Name = summary.Name,
            Kind = summary.Kind,
            Protocol = summary.Protocol,
            PublicEndpoint = summary.PublicEndpoint,
            PrivateService = summary.PrivateService,
            Status = summary.Status,
            StartedAt = summary.StartedAt
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task MarkStoppedAsync(string id, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProxyNetworkerDbContext>();
        var record = await db.Tunnels.FindAsync([id], cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return;
        }

        record.Status = "stopped";
        record.StoppedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static PortTunnelProtocol ParseProtocol(string protocol)
    {
        return protocol.ToLowerInvariant() switch
        {
            "tcp" => PortTunnelProtocol.Tcp,
            "udp" => PortTunnelProtocol.Udp,
            _ => throw new ArgumentException($"Unsupported protocol '{protocol}'. Use tcp or udp.", nameof(protocol))
        };
    }

    private static IPEndPoint ParsePublicEndpoint(string address, int port)
    {
        if (!IPAddress.TryParse(address, out var ipAddress))
        {
            throw new ArgumentException($"Listen address must be an IP address: '{address}'.", nameof(address));
        }

        return new IPEndPoint(ipAddress, ValidatePort(port, nameof(port)));
    }

    private static int ValidatePort(int port, string parameterName)
    {
        if (port <= 0 || port > 65535)
        {
            throw new ArgumentOutOfRangeException(parameterName, port, "Port must be between 1 and 65535.");
        }

        return port;
    }

    private static string NormalizeName(string? name, string prefix)
    {
        return string.IsNullOrWhiteSpace(name)
            ? $"{prefix}-{Guid.NewGuid():N}"[..24]
            : name.Trim();
    }

    private static string NewId()
    {
        return Guid.NewGuid().ToString("N");
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

    private sealed record ActiveTunnel(TunnelSummary Summary, IDisposable Runtime);
}
