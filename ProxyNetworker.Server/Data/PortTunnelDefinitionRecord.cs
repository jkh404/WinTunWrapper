namespace ProxyNetworker.Server.Data;

public sealed class PortTunnelDefinitionRecord
{
    public string Id { get; set; } = string.Empty;

    public string OwnerUserId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Protocol { get; set; } = "tcp";

    public string PrivateHost { get; set; } = "127.0.0.1";

    public int PrivatePort { get; set; }

    public int PublicPort { get; set; }

    public long BandwidthLimitBytes { get; set; }

    public long MaxTrafficSpeedBytesPerSecond { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
