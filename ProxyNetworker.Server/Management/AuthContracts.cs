namespace ProxyNetworker.Server.Management;

public sealed class LoginRequest
{
    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;
}

public sealed class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = string.Empty;

    public string NewPassword { get; set; } = string.Empty;
}

public sealed record AccountResponse(
    string Id,
    string Username,
    string Role,
    bool IsDisabled,
    int MaxVirtualNetworks,
    int MaxPortTunnels,
    int PortRangeStart,
    int PortRangeEnd,
    long BandwidthLimitBytes,
    long MaxTrafficSpeedBytesPerSecond,
    DateTimeOffset CreatedAt,
    DateTimeOffset PasswordChangedAt);

public sealed class CreateUserRequest
{
    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string Role { get; set; } = Data.UserRoles.User;

    public int MaxVirtualNetworks { get; set; } = 1;

    public int MaxPortTunnels { get; set; } = 3;

    public int PortRangeStart { get; set; } = 20000;

    public int PortRangeEnd { get; set; } = 30000;

    public long BandwidthLimitBytes { get; set; }

    public long MaxTrafficSpeedBytesPerSecond { get; set; }
}

public sealed class UpdateUserRequest
{
    public string Role { get; set; } = Data.UserRoles.User;

    public bool IsDisabled { get; set; }

    public int MaxVirtualNetworks { get; set; } = 1;

    public int MaxPortTunnels { get; set; } = 3;

    public int PortRangeStart { get; set; } = 20000;

    public int PortRangeEnd { get; set; } = 30000;

    public long BandwidthLimitBytes { get; set; }

    public long MaxTrafficSpeedBytesPerSecond { get; set; }
}

public sealed class ResetPasswordRequest
{
    public string NewPassword { get; set; } = string.Empty;
}
