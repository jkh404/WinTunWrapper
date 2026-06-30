namespace ProxyNetworker.Server.Management;

public sealed class CreateVirtualNetworkDefinitionRequest
{
    public string Name { get; set; } = string.Empty;

    public string GatewayAddress { get; set; } = "10.66.0.1";

    public int PrefixLength { get; set; } = 24;

    public int ListenPort { get; set; }

    public int Mtu { get; set; } = 1400;
}

public sealed record VirtualNetworkDefinitionResponse(
    string Id,
    string OwnerUserId,
    string Name,
    string GatewayAddress,
    int PrefixLength,
    int ListenPort,
    int Mtu,
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
    bool HasStoredValue,
    DateTimeOffset CreatedAt);

public sealed record CreatedAccessTokenResponse(
    AccessTokenResponse Token,
    string PlainTextToken);

public sealed record AccessTokenValueResponse(string PlainTextToken);

public sealed class UpdateSystemSettingsRequest
{
    public int PublicPortRangeStart { get; set; } = 1;

    public int PublicPortRangeEnd { get; set; } = 65535;

    public long MaxBandwidthLimitBytes { get; set; }

    public long MaxTrafficSpeedBytesPerSecond { get; set; }
}

public sealed record SystemSettingsResponse(
    int PublicPortRangeStart,
    int PublicPortRangeEnd,
    long MaxBandwidthLimitBytes,
    long MaxTrafficSpeedBytesPerSecond,
    DateTimeOffset UpdatedAt);
