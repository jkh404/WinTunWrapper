using System.Net;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using ProxyNetworker.Server.Data;
using ProxyNetworker.Server.Security;

namespace ProxyNetworker.Server.VirtualNetworks;

public sealed class VirtualNetworkAccessService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TokenGenerator _tokenGenerator;

    public VirtualNetworkAccessService(IServiceScopeFactory scopeFactory, TokenGenerator tokenGenerator)
    {
        _scopeFactory = scopeFactory;
        _tokenGenerator = tokenGenerator;
    }

    public async Task<VirtualNetworkAccessGrant?> ValidateVirtualNetworkTokenAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProxyNetworkerDbContext>();
        var tokenHash = _tokenGenerator.HashToken(token.Trim());
        var accessToken = await db.AccessTokens
            .FirstOrDefaultAsync(item => item.TokenHash == tokenHash && item.ScopeKind == AccessTokenScopes.VirtualNetwork, cancellationToken)
            .ConfigureAwait(false);

        if (accessToken is null || !IsTokenUsable(accessToken))
        {
            return null;
        }

        var virtualNetwork = await db.VirtualNetworks
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == accessToken.ResourceId, cancellationToken)
            .ConfigureAwait(false);
        if (virtualNetwork is null)
        {
            return null;
        }

        var owner = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == virtualNetwork.OwnerUserId, cancellationToken)
            .ConfigureAwait(false);
        if (owner is null || owner.IsDisabled)
        {
            return null;
        }

        var peerTokens = await db.AccessTokens
            .AsNoTracking()
            .Where(item => item.ScopeKind == AccessTokenScopes.VirtualNetwork && item.ResourceId == virtualNetwork.Id)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        var clientAddress = TryAssignClientAddress(virtualNetwork, accessToken.Id, peerTokens);
        if (clientAddress is null)
        {
            return null;
        }

        if (accessToken.TokenType == AccessTokenTypes.OneTime)
        {
            accessToken.IsConsumed = true;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new VirtualNetworkAccessGrant(virtualNetwork, accessToken.Id, clientAddress);
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

    private static IPAddress? TryAssignClientAddress(
        VirtualNetworkRecord virtualNetwork,
        string currentTokenId,
        IEnumerable<AccessTokenRecord> tokens)
    {
        if (!IPAddress.TryParse(virtualNetwork.GatewayAddress, out var gatewayAddress) ||
            gatewayAddress.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return null;
        }

        var gateway = IPv4ToUInt32(gatewayAddress);
        var mask = PrefixMask(virtualNetwork.PrefixLength);
        var network = gateway & mask;
        var broadcast = network | ~mask;
        if (broadcast <= network + 1)
        {
            return null;
        }

        var usableStart = network + 1;
        var usableEnd = broadcast - 1;
        if (gateway < usableStart || gateway > usableEnd)
        {
            return null;
        }

        var usableCount = (ulong)usableEnd - usableStart + 1;
        var assigned = new HashSet<uint>();
        var orderedTokens = tokens
            .Where(IsTokenUsable)
            .OrderBy(static item => item.CreatedAt)
            .ThenBy(static item => item.Id, StringComparer.Ordinal)
            .ToArray();

        foreach (var token in orderedTokens)
        {
            var candidate = PickAddress(virtualNetwork.Id, token.Id, usableStart, usableCount, gateway, assigned, orderedTokens.Length);
            if (candidate is null)
            {
                return null;
            }

            assigned.Add(candidate.Value);
            if (token.Id == currentTokenId)
            {
                return UInt32ToIPv4(candidate.Value);
            }
        }

        return null;
    }

    private static uint? PickAddress(
        string networkId,
        string tokenId,
        uint usableStart,
        ulong usableCount,
        uint gateway,
        HashSet<uint> assigned,
        int tokenCount)
    {
        var offset = StableOffset(networkId, tokenId, usableCount);
        var maxAttempts = Math.Min(usableCount, (ulong)tokenCount + 2);
        for (ulong attempt = 0; attempt < maxAttempts; attempt++)
        {
            var candidate = usableStart + (uint)((offset + attempt) % usableCount);
            if (candidate == gateway || assigned.Contains(candidate))
            {
                continue;
            }

            return candidate;
        }

        return null;
    }

    private static ulong StableOffset(string networkId, string tokenId, ulong modulo)
    {
        var data = System.Text.Encoding.UTF8.GetBytes($"{networkId}:{tokenId}");
        var hash = SHA256.HashData(data);
        var value = BitConverter.ToUInt64(hash, 0);
        return modulo == 0 ? 0 : value % modulo;
    }

    private static uint PrefixMask(int prefixLength)
    {
        return prefixLength == 0 ? 0 : uint.MaxValue << (32 - prefixLength);
    }

    private static uint IPv4ToUInt32(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }

    private static IPAddress UInt32ToIPv4(uint value)
    {
        return new IPAddress(new[]
        {
            (byte)(value >> 24),
            (byte)(value >> 16),
            (byte)(value >> 8),
            (byte)value
        });
    }
}

public sealed record VirtualNetworkAccessGrant(
    VirtualNetworkRecord Definition,
    string TokenId,
    IPAddress ClientAddress);
