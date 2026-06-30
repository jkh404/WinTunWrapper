using Microsoft.EntityFrameworkCore;
using ProxyNetworker.Server.Security;

namespace ProxyNetworker.Server.Data;

public sealed class DatabaseInitializer : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(IServiceProvider serviceProvider, ILogger<DatabaseInitializer> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProxyNetworkerDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<PasswordHasher>();
        await db.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
        await EnsureDefaultAdminAsync(db, passwordHasher, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("SQLite database is ready at {ConnectionString}.", db.Database.GetConnectionString());
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
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
