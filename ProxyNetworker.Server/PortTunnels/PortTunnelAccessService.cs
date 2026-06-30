using Microsoft.EntityFrameworkCore;
using ProxyNetworker.Server.Data;
using ProxyNetworker.Server.Security;

namespace ProxyNetworker.Server.PortTunnels;

public sealed class PortTunnelAccessService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TokenGenerator _tokenGenerator;

    public PortTunnelAccessService(IServiceScopeFactory scopeFactory, TokenGenerator tokenGenerator)
    {
        _scopeFactory = scopeFactory;
        _tokenGenerator = tokenGenerator;
    }

    public async Task<PortTunnelAccessGrant?> ValidatePortTunnelTokenAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProxyNetworkerDbContext>();
        var tokenHash = _tokenGenerator.HashToken(token.Trim());
        var accessToken = await db.AccessTokens
            .FirstOrDefaultAsync(item => item.TokenHash == tokenHash && item.ScopeKind == AccessTokenScopes.PortTunnel, cancellationToken)
            .ConfigureAwait(false);

        if (accessToken is null || !IsTokenUsable(accessToken))
        {
            return null;
        }

        var tunnel = await db.PortTunnelDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == accessToken.ResourceId, cancellationToken)
            .ConfigureAwait(false);
        if (tunnel is null)
        {
            return null;
        }

        var owner = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == tunnel.OwnerUserId, cancellationToken)
            .ConfigureAwait(false);
        if (owner is null || owner.IsDisabled)
        {
            return null;
        }

        if (accessToken.TokenType == AccessTokenTypes.OneTime)
        {
            accessToken.IsConsumed = true;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new PortTunnelAccessGrant(tunnel, accessToken.Id);
    }

    private static bool IsTokenUsable(AccessTokenRecord token)
    {
        var now = DateTimeOffset.UtcNow;
        if (token.IsConsumed)
        {
            return false;
        }

        if (token.ValidFrom is not null && token.ValidFrom > now)
        {
            return false;
        }

        if (token.ValidUntil is not null && token.ValidUntil <= now)
        {
            return false;
        }

        return true;
    }
}

public sealed record PortTunnelAccessGrant(PortTunnelDefinitionRecord Definition, string TokenId);
