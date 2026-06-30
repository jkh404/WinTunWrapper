namespace ProxyNetworker.Core.PortTunnels.Remote;

public sealed class RemotePortTunnelConfig
{
    public string TunnelId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Protocol { get; set; } = "tcp";

    public string PrivateHost { get; set; } = "127.0.0.1";

    public int PrivatePort { get; set; }

    public int PublicPort { get; set; }
}
