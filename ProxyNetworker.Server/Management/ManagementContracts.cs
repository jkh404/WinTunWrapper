namespace ProxyNetworker.Server.Management;

public sealed record ServerInfoResponse(
    string Name,
    string Role,
    string[] Capabilities);

public sealed record HealthResponse(
    string Status,
    DateTimeOffset CheckedAt);

public sealed record ErrorResponse(string Message);

public sealed class CreatePortTunnelRequest
{
    public string? Name { get; set; }

    public string Protocol { get; set; } = "tcp";

    public string ListenAddress { get; set; } = string.Empty;

    public int ListenPort { get; set; }

    public string TargetHost { get; set; } = "127.0.0.1";

    public int TargetPort { get; set; } = 80;
}

public sealed class CreateVirtualNetworkRequest
{
    public string? Name { get; set; }

    public int ListenPort { get; set; }

    public string TunAddress { get; set; } = string.Empty;

    public int PrefixLength { get; set; }

    public int Mtu { get; set; }
}

public sealed record TunnelSummary(
    string Id,
    string Name,
    string Kind,
    string? Protocol,
    string PublicEndpoint,
    string? PrivateService,
    DateTimeOffset StartedAt,
    string Status);
