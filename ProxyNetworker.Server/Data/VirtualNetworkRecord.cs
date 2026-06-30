namespace ProxyNetworker.Server.Data;

public sealed class VirtualNetworkRecord
{
    public string Id { get; set; } = string.Empty;

    public string OwnerUserId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string GatewayAddress { get; set; } = "10.66.0.1";

    public int PrefixLength { get; set; } = 24;

    public int ListenPort { get; set; } = 51820;

    public int Mtu { get; set; } = 1400;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
