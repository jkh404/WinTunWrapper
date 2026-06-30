using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProxyNetworker.Server.Management;
using ProxyNetworker.Server.Security;

namespace ProxyNetworker.Server.Data;

public sealed class DatabaseInitializer : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DatabaseInitializer> _logger;
    private readonly SystemSettingsOptions _systemSettingsOptions;

    public DatabaseInitializer(
        IServiceProvider serviceProvider,
        ILogger<DatabaseInitializer> logger,
        IOptions<SystemSettingsOptions> systemSettingsOptions)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _systemSettingsOptions = systemSettingsOptions.Value;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProxyNetworkerDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<PasswordHasher>();
        await db.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
        await EnsureVirtualNetworkColumnsAsync(db, cancellationToken).ConfigureAwait(false);
        await EnsureAccessTokenColumnsAsync(db, cancellationToken).ConfigureAwait(false);
        await EnsureSystemSettingsAsync(db, _systemSettingsOptions, cancellationToken).ConfigureAwait(false);
        await EnsureDefaultAdminAsync(db, passwordHasher, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("SQLite database is ready at {ConnectionString}.", db.Database.GetConnectionString());
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private static async Task EnsureVirtualNetworkColumnsAsync(
        ProxyNetworkerDbContext db,
        CancellationToken cancellationToken)
    {
        if (!await ColumnExistsAsync(db, "VirtualNetworks", "ListenPort", cancellationToken).ConfigureAwait(false))
        {
            await db.Database.ExecuteSqlRawAsync(
                """ALTER TABLE "VirtualNetworks" ADD COLUMN "ListenPort" INTEGER NOT NULL DEFAULT 51820;""",
                cancellationToken).ConfigureAwait(false);
        }

        if (!await ColumnExistsAsync(db, "VirtualNetworks", "Mtu", cancellationToken).ConfigureAwait(false))
        {
            await db.Database.ExecuteSqlRawAsync(
                """ALTER TABLE "VirtualNetworks" ADD COLUMN "Mtu" INTEGER NOT NULL DEFAULT 1400;""",
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task EnsureAccessTokenColumnsAsync(
        ProxyNetworkerDbContext db,
        CancellationToken cancellationToken)
    {
        if (!await ColumnExistsAsync(db, "AccessTokens", "ProtectedTokenValue", cancellationToken).ConfigureAwait(false))
        {
            await db.Database.ExecuteSqlRawAsync(
                """ALTER TABLE "AccessTokens" ADD COLUMN "ProtectedTokenValue" TEXT NULL;""",
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<bool> ColumnExistsAsync(
        ProxyNetworkerDbContext db,
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = $"""PRAGMA table_info("{tableName}")""";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task EnsureSystemSettingsAsync(
        ProxyNetworkerDbContext db,
        SystemSettingsOptions options,
        CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "SystemSettings" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_SystemSettings" PRIMARY KEY,
                "PublicPortRangeStart" INTEGER NOT NULL,
                "PublicPortRangeEnd" INTEGER NOT NULL,
                "MaxBandwidthLimitBytes" INTEGER NOT NULL,
                "MaxTrafficSpeedBytesPerSecond" INTEGER NOT NULL,
                "UpdatedAt" TEXT NOT NULL
            );
            """,
            cancellationToken).ConfigureAwait(false);

        if (await db.SystemSettings.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        db.SystemSettings.Add(new SystemSettingsRecord
        {
            Id = SystemSettingsIds.Default,
            PublicPortRangeStart = options.PublicPortRangeStart,
            PublicPortRangeEnd = options.PublicPortRangeEnd,
            MaxBandwidthLimitBytes = options.MaxBandwidthLimitBytes,
            MaxTrafficSpeedBytesPerSecond = options.MaxTrafficSpeedBytesPerSecond,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureDefaultAdminAsync(
        ProxyNetworkerDbContext db,
        PasswordHasher passwordHasher,
        CancellationToken cancellationToken)
    {
        if (await db.Users.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        db.Users.Add(new UserAccount
        {
            Id = Guid.NewGuid().ToString("N"),
            Username = "sky",
            PasswordHash = passwordHasher.Hash("sky.123"),
            Role = UserRoles.Admin,
            MaxVirtualNetworks = int.MaxValue,
            MaxPortTunnels = int.MaxValue,
            PortRangeStart = 1,
            PortRangeEnd = 65535,
            BandwidthLimitBytes = 0,
            MaxTrafficSpeedBytesPerSecond = 0,
            CreatedAt = DateTimeOffset.UtcNow,
            PasswordChangedAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
