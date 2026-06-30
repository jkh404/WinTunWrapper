using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace ProxyNetworker.Server.Management;

public sealed class SystemSettingsOptions
{
    public const string Section = "SystemSettings";

    [Range(1, 65535)]
    public int PublicPortRangeStart { get; init; } = 1;

    [Range(1, 65535)]
    public int PublicPortRangeEnd { get; init; } = 65535;

    [Range(0, long.MaxValue)]
    public long MaxBandwidthLimitBytes { get; init; }

    [Range(0, long.MaxValue)]
    public long MaxTrafficSpeedBytesPerSecond { get; init; }
}

public sealed class SystemSettingsOptionsValidator : IValidateOptions<SystemSettingsOptions>
{
    public ValidateOptionsResult Validate(string? name, SystemSettingsOptions options)
    {
        if (options.PublicPortRangeStart > options.PublicPortRangeEnd)
        {
            return ValidateOptionsResult.Fail("SystemSettings:PublicPortRangeStart must be less than or equal to SystemSettings:PublicPortRangeEnd.");
        }

        return ValidateOptionsResult.Success;
    }
}
