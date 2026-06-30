namespace ProxyNetworker.Server.Data;

public sealed class UserAccount
{
    public string Id { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public string Role { get; set; } = UserRoles.User;

    public bool IsDisabled { get; set; }

    public int MaxVirtualNetworks { get; set; } = 1;

    public int MaxPortTunnels { get; set; } = 3;

    public int PortRangeStart { get; set; } = 20000;

    public int PortRangeEnd { get; set; } = 30000;

    public long BandwidthLimitBytes { get; set; } = 0;

    public long MaxTrafficSpeedBytesPerSecond { get; set; } = 0;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset PasswordChangedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class UserRoles
{
    public const string Admin = "Admin";
    public const string User = "User";
}
