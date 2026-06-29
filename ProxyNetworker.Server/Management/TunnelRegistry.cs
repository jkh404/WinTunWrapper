using System.Collections.Concurrent;
using System.Net;
using ProxyNetworker.Core;
using ProxyNetworker.Core.PortTunnels;

namespace ProxyNetworker.Server.Management;

public sealed class TunnelRegistry : IDisposable
{
    private readonly ConcurrentDictionary<string, ActiveTunnel> _tunnels = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<TunnelRegistry> _logger;

    public TunnelRegistry(ILogger<TunnelRegistry> logger)
    {
        _logger = logger;
    }

    public IReadOnlyCollection<TunnelSummary> List()
    {
        return _tunnels.Values
            .Select(static item => item.Summary)
            .OrderBy(static item => item.StartedAt)
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
            StartedAt: DateTimeOffset.UtcNow);

        Add(summary, tunnel);
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
                    BindEndPoint = listen
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
                StartedAt: DateTimeOffset.UtcNow);

            Add(summary, tunnel);
            return summary;
        }
        catch
        {
            tunnel?.Dispose();
            tunDevice.Dispose();
            throw;
        }
    }

    public bool Stop(string id)
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

    private void Add(TunnelSummary summary, IDisposable runtime)
    {
        if (!_tunnels.TryAdd(summary.Id, new ActiveTunnel(summary, runtime)))
        {
            runtime.Dispose();
            throw new InvalidOperationException($"Tunnel id collision: {summary.Id}.");
        }
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
