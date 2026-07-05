namespace ProxyNetworker.Server.Data;

public sealed class VirtualNetworkRecord
{
    public string Id { get; set; } = string.Empty;

    public string OwnerUserId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string GatewayAddress { get; set; } = string.Empty;

    public int PrefixLength { get; set; }

    public int ListenPort { get; set; }

    public int Mtu { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
