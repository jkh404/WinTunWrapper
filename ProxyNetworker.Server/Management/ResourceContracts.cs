namespace ProxyNetworker.Server.Management;

public sealed class CreateVirtualNetworkDefinitionRequest
{
    public string Name { get; set; } = string.Empty;

    public string GatewayAddress { get; set; } = "10.66.0.1";

    public int PrefixLength { get; set; } = 24;
}

public sealed record VirtualNetworkDefinitionResponse(
    string Id,
    string OwnerUserId,
    string Name,
    string GatewayAddress,
    int PrefixLength,
    DateTimeOffset CreatedAt);

public sealed class CreatePortTunnelDefinitionRequest
{
    public string Name { get; set; } = string.Empty;

    public string Protocol { get; set; } = "tcp";

    public string PrivateHost { get; set; } = "127.0.0.1";

    public int PrivatePort { get; set; }
}

public sealed record PortTunnelDefinitionResponse(
    string Id,
    string OwnerUserId,
    string Name,
    string Protocol,
    string PrivateHost,
    int PrivatePort,
    int PublicPort,
    long BandwidthLimitBytes,
    long MaxTrafficSpeedBytesPerSecond,
    DateTimeOffset CreatedAt);

public sealed class CreateAccessTokenRequest
{
    public string TokenType { get; set; } = Data.AccessTokenTypes.Permanent;

    public DateTimeOffset? ValidFrom { get; set; }

    public DateTimeOffset? ValidUntil { get; set; }
}

public sealed record AccessTokenResponse(
    string Id,
    string ScopeKind,
    string ResourceId,
    string TokenPreview,
    string TokenType,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidUntil,
    bool IsConsumed,
    DateTimeOffset CreatedAt);

public sealed record CreatedAccessTokenResponse(
    AccessTokenResponse Token,
    string PlainTextToken);
