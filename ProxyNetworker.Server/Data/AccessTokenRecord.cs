namespace ProxyNetworker.Server.Data;

public sealed class AccessTokenRecord
{
    public string Id { get; set; } = string.Empty;

    public string ScopeKind { get; set; } = string.Empty;

    public string ResourceId { get; set; } = string.Empty;

    public string TokenHash { get; set; } = string.Empty;

    public string TokenPreview { get; set; } = string.Empty;

    public string TokenType { get; set; } = AccessTokenTypes.Permanent;

    public DateTimeOffset? ValidFrom { get; set; }

    public DateTimeOffset? ValidUntil { get; set; }

    public bool IsConsumed { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public string CreatedByUserId { get; set; } = string.Empty;
}

public static class AccessTokenTypes
{
    public const string Permanent = "Permanent";
    public const string Limited = "Limited";
    public const string OneTime = "OneTime";
}

public static class AccessTokenScopes
{
    public const string VirtualNetwork = "VirtualNetwork";
    public const string PortTunnel = "PortTunnel";
}
