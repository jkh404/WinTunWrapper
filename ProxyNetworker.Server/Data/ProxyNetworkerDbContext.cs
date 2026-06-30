using Microsoft.EntityFrameworkCore;

namespace ProxyNetworker.Server.Data;

public sealed class ProxyNetworkerDbContext : DbContext
{
    public ProxyNetworkerDbContext(DbContextOptions<ProxyNetworkerDbContext> options)
        : base(options)
    {
    }

    public DbSet<TunnelRecord> Tunnels => Set<TunnelRecord>();

    public DbSet<UserAccount> Users => Set<UserAccount>();

    public DbSet<VirtualNetworkRecord> VirtualNetworks => Set<VirtualNetworkRecord>();

    public DbSet<PortTunnelDefinitionRecord> PortTunnelDefinitions => Set<PortTunnelDefinitionRecord>();

    public DbSet<AccessTokenRecord> AccessTokens => Set<AccessTokenRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var tunnel = modelBuilder.Entity<TunnelRecord>();
        tunnel.HasKey(item => item.Id);
        tunnel.Property(item => item.Id).HasMaxLength(32);
        tunnel.Property(item => item.Name).HasMaxLength(128).IsRequired();
        tunnel.Property(item => item.Kind).HasMaxLength(32).IsRequired();
        tunnel.Property(item => item.Protocol).HasMaxLength(16);
        tunnel.Property(item => item.PublicEndpoint).HasMaxLength(256).IsRequired();
        tunnel.Property(item => item.PrivateService).HasMaxLength(256);
        tunnel.Property(item => item.Status).HasMaxLength(32).IsRequired();
        tunnel.HasIndex(item => item.Name);
        tunnel.HasIndex(item => item.StartedAt);

        var user = modelBuilder.Entity<UserAccount>();
        user.HasKey(item => item.Id);
        user.Property(item => item.Id).HasMaxLength(32);
        user.Property(item => item.Username).HasMaxLength(64).IsRequired();
        user.Property(item => item.PasswordHash).HasMaxLength(512).IsRequired();
        user.Property(item => item.Role).HasMaxLength(32).IsRequired();
        user.HasIndex(item => item.Username).IsUnique();

        var virtualNetwork = modelBuilder.Entity<VirtualNetworkRecord>();
        virtualNetwork.HasKey(item => item.Id);
        virtualNetwork.Property(item => item.Id).HasMaxLength(32);
        virtualNetwork.Property(item => item.OwnerUserId).HasMaxLength(32).IsRequired();
        virtualNetwork.Property(item => item.Name).HasMaxLength(128).IsRequired();
        virtualNetwork.Property(item => item.GatewayAddress).HasMaxLength(64).IsRequired();
        virtualNetwork.HasIndex(item => item.OwnerUserId);

        var portTunnel = modelBuilder.Entity<PortTunnelDefinitionRecord>();
        portTunnel.HasKey(item => item.Id);
        portTunnel.Property(item => item.Id).HasMaxLength(32);
        portTunnel.Property(item => item.OwnerUserId).HasMaxLength(32).IsRequired();
        portTunnel.Property(item => item.Name).HasMaxLength(128).IsRequired();
        portTunnel.Property(item => item.Protocol).HasMaxLength(16).IsRequired();
        portTunnel.Property(item => item.PrivateHost).HasMaxLength(256).IsRequired();
        portTunnel.HasIndex(item => item.OwnerUserId);
        portTunnel.HasIndex(item => item.PublicPort).IsUnique();

        var accessToken = modelBuilder.Entity<AccessTokenRecord>();
        accessToken.HasKey(item => item.Id);
        accessToken.Property(item => item.Id).HasMaxLength(32);
        accessToken.Property(item => item.ScopeKind).HasMaxLength(32).IsRequired();
        accessToken.Property(item => item.ResourceId).HasMaxLength(32).IsRequired();
        accessToken.Property(item => item.TokenHash).HasMaxLength(128).IsRequired();
        accessToken.Property(item => item.TokenPreview).HasMaxLength(24).IsRequired();
        accessToken.Property(item => item.TokenType).HasMaxLength(32).IsRequired();
        accessToken.Property(item => item.CreatedByUserId).HasMaxLength(32).IsRequired();
        accessToken.HasIndex(item => item.TokenHash).IsUnique();
        accessToken.HasIndex(item => new { item.ScopeKind, item.ResourceId });
    }
}
