namespace ProxyNetworker.Server.Data;

public sealed class TunnelRecord
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string? Protocol { get; set; }

    public string PublicEndpoint { get; set; } = string.Empty;

    public string? PrivateService { get; set; }

    public string Status { get; set; } = "running";

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? StoppedAt { get; set; }
}
