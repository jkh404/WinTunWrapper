namespace ProxyNetworker.Server.Data;

public sealed class SystemSettingsRecord
{
    public string Id { get; set; } = SystemSettingsIds.Default;

    public int PublicPortRangeStart { get; set; } = 1;

    public int PublicPortRangeEnd { get; set; } = 65535;

    public long MaxBandwidthLimitBytes { get; set; }

    public long MaxTrafficSpeedBytesPerSecond { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class SystemSettingsIds
{
    public const string Default = "default";
}
