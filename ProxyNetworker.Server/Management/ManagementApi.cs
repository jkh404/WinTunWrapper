using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using ProxyNetworker.Server.Data;
using ProxyNetworker.Server.Security;

namespace ProxyNetworker.Server.Management;

public static class ManagementApi
{
    public static IEndpointRouteBuilder MapManagementApi(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth");
        auth.MapPost("/login", Login);
        auth.MapPost("/logout", async (HttpContext httpContext) => await Logout(httpContext).ConfigureAwait(false)).RequireAuthorization();
        auth.MapGet("/me", Me).RequireAuthorization();
        auth.MapPost("/change-password", ChangePassword).RequireAuthorization();

        var users = app.MapGroup("/api/users").RequireAuthorization("AdminOnly");
        users.MapGet("/", ListUsers);
        users.MapPost("/", CreateUser);
        users.MapPut("/{id}", UpdateUser);
        users.MapPost("/{id}/reset-password", ResetPassword);

        var virtualNetworks = app.MapGroup("/api/virtual-networks").RequireAuthorization();
        virtualNetworks.MapGet("/", ListVirtualNetworks);
        virtualNetworks.MapPost("/", CreateVirtualNetwork);
        virtualNetworks.MapDelete("/{id}", DeleteVirtualNetwork);
        virtualNetworks.MapGet("/{id}/tokens", ListVirtualNetworkTokens);
        virtualNetworks.MapPost("/{id}/tokens", CreateVirtualNetworkToken);

        var portTunnels = app.MapGroup("/api/port-tunnels").RequireAuthorization();
        portTunnels.MapGet("/", ListPortTunnels);
        portTunnels.MapPost("/", CreatePortTunnelDefinition);
        portTunnels.MapDelete("/{id}", DeletePortTunnelDefinition);
        portTunnels.MapGet("/{id}/tokens", ListPortTunnelTokens);
        portTunnels.MapPost("/{id}/tokens", CreatePortTunnelToken);

        return app;
    }

    private static async Task<IResult> Login(
        LoginRequest request,
        ProxyNetworkerDbContext db,
        PasswordHasher passwordHasher,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(
            item => item.Username == request.Username,
            cancellationToken).ConfigureAwait(false);

        if (user is null || user.IsDisabled || !passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            return TypedResults.Unauthorized();
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.Role)
        };

        await httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)),
            new AuthenticationProperties
            {
                IsPersistent = true,
                IssuedUtc = DateTimeOffset.UtcNow
            }).ConfigureAwait(false);

        return TypedResults.Ok(ToAccountResponse(user));
    }

    private static async Task<IResult> Logout(HttpContext httpContext)
    {
        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> Me(
        ClaimsPrincipal principal,
        ProxyNetworkerDbContext db,
        CancellationToken cancellationToken)
    {
        var id = AccountContext.UserId(principal);
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id, cancellationToken).ConfigureAwait(false);
        return user is null ? TypedResults.Unauthorized() : TypedResults.Ok(ToAccountResponse(user));
    }

    private static async Task<IResult> ChangePassword(
        ChangePasswordRequest request,
        ClaimsPrincipal principal,
        ProxyNetworkerDbContext db,
        PasswordHasher passwordHasher,
        CancellationToken cancellationToken)
    {
        var id = AccountContext.UserId(principal);
        var user = await db.Users.FirstOrDefaultAsync(item => item.Id == id, cancellationToken).ConfigureAwait(false);
        if (user is null || !passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
        {
            return TypedResults.BadRequest(new ErrorResponse("Current password is incorrect."));
        }

        user.PasswordHash = passwordHasher.Hash(request.NewPassword);
        user.PasswordChangedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> ListUsers(ProxyNetworkerDbContext db, CancellationToken cancellationToken)
    {
        var users = await db.Users.AsNoTracking().OrderBy(item => item.Username).ToArrayAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(users.Select(ToAccountResponse).ToArray());
    }

    private static async Task<IResult> CreateUser(
        CreateUserRequest request,
        ProxyNetworkerDbContext db,
        PasswordHasher passwordHasher,
        CancellationToken cancellationToken)
    {
        var validation = ValidateUserInputs(request.Username, request.Role, request.PortRangeStart, request.PortRangeEnd);
        if (validation is not null)
        {
            return TypedResults.BadRequest(validation);
        }

        if (await db.Users.AnyAsync(item => item.Username == request.Username, cancellationToken).ConfigureAwait(false))
        {
            return TypedResults.BadRequest(new ErrorResponse("Username already exists."));
        }

        var user = new UserAccount
        {
            Id = Guid.NewGuid().ToString("N"),
            Username = request.Username.Trim(),
            PasswordHash = passwordHasher.Hash(request.Password),
            Role = request.Role,
            MaxVirtualNetworks = request.MaxVirtualNetworks,
            MaxPortTunnels = request.MaxPortTunnels,
            PortRangeStart = request.PortRangeStart,
            PortRangeEnd = request.PortRangeEnd,
            BandwidthLimitBytes = Math.Max(0, request.BandwidthLimitBytes),
            MaxTrafficSpeedBytesPerSecond = Math.Max(0, request.MaxTrafficSpeedBytesPerSecond),
            CreatedAt = DateTimeOffset.UtcNow,
            PasswordChangedAt = DateTimeOffset.UtcNow
        };

        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.Created($"/api/users/{user.Id}", ToAccountResponse(user));
    }

    private static async Task<IResult> UpdateUser(
        string id,
        UpdateUserRequest request,
        ProxyNetworkerDbContext db,
        CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(item => item.Id == id, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return TypedResults.NotFound(new ErrorResponse("User was not found."));
        }

        var validation = ValidateUserInputs(user.Username, request.Role, request.PortRangeStart, request.PortRangeEnd);
        if (validation is not null)
        {
            return TypedResults.BadRequest(validation);
        }

        user.Role = request.Role;
        user.IsDisabled = request.IsDisabled;
        user.MaxVirtualNetworks = request.MaxVirtualNetworks;
        user.MaxPortTunnels = request.MaxPortTunnels;
        user.PortRangeStart = request.PortRangeStart;
        user.PortRangeEnd = request.PortRangeEnd;
        user.BandwidthLimitBytes = Math.Max(0, request.BandwidthLimitBytes);
        user.MaxTrafficSpeedBytesPerSecond = Math.Max(0, request.MaxTrafficSpeedBytesPerSecond);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(ToAccountResponse(user));
    }

    private static async Task<IResult> ResetPassword(
        string id,
        ResetPasswordRequest request,
        ProxyNetworkerDbContext db,
        PasswordHasher passwordHasher,
        CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(item => item.Id == id, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return TypedResults.NotFound(new ErrorResponse("User was not found."));
        }

        user.PasswordHash = passwordHasher.Hash(request.NewPassword);
        user.PasswordChangedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> ListVirtualNetworks(
        ClaimsPrincipal principal,
        ProxyNetworkerDbContext db,
        CancellationToken cancellationToken)
    {
        var query = db.VirtualNetworks.AsNoTracking();
        if (!AccountContext.IsAdmin(principal))
        {
            var userId = AccountContext.UserId(principal);
            query = query.Where(item => item.OwnerUserId == userId);
        }

        var items = await query.ToArrayAsync(cancellationToken).ConfigureAwait(false);
        items = items.OrderBy(item => item.CreatedAt).ToArray();
        return TypedResults.Ok(items.Select(ToVirtualNetworkResponse).ToArray());
    }

    private static async Task<IResult> CreateVirtualNetwork(
        CreateVirtualNetworkDefinitionRequest request,
        ClaimsPrincipal principal,
        ProxyNetworkerDbContext db,
        CancellationToken cancellationToken)
    {
        var user = await CurrentUserAsync(principal, db, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!AccountContext.IsAdmin(principal))
        {
            var existing = await db.VirtualNetworks.CountAsync(item => item.OwnerUserId == user.Id, cancellationToken).ConfigureAwait(false);
            if (existing >= user.MaxVirtualNetworks)
            {
                return TypedResults.BadRequest(new ErrorResponse("Virtual Network quota exceeded."));
            }
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return TypedResults.BadRequest(new ErrorResponse("Name cannot be empty."));
        }

        if (!System.Net.IPAddress.TryParse(request.GatewayAddress, out _))
        {
            return TypedResults.BadRequest(new ErrorResponse("Gateway address is invalid."));
        }

        if (request.PrefixLength < 1 || request.PrefixLength > 32)
        {
            return TypedResults.BadRequest(new ErrorResponse("Prefix length must be between 1 and 32."));
        }

        var record = new VirtualNetworkRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            OwnerUserId = user.Id,
            Name = request.Name.Trim(),
            GatewayAddress = request.GatewayAddress,
            PrefixLength = request.PrefixLength,
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.VirtualNetworks.Add(record);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.Created($"/api/virtual-networks/{record.Id}", ToVirtualNetworkResponse(record));
    }

    private static async Task<IResult> DeleteVirtualNetwork(
        string id,
        ClaimsPrincipal principal,
        ProxyNetworkerDbContext db,
        CancellationToken cancellationToken)
    {
        var record = await FindVirtualNetworkAsync(id, principal, db, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return TypedResults.NotFound(new ErrorResponse("Virtual Network was not found."));
        }

        var tokens = await db.AccessTokens
            .Where(item => item.ScopeKind == AccessTokenScopes.VirtualNetwork && item.ResourceId == id)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        db.AccessTokens.RemoveRange(tokens);
        db.VirtualNetworks.Remove(record);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> ListPortTunnels(
        ClaimsPrincipal principal,
        ProxyNetworkerDbContext db,
        CancellationToken cancellationToken)
    {
        var query = db.PortTunnelDefinitions.AsNoTracking();
        if (!AccountContext.IsAdmin(principal))
        {
            var userId = AccountContext.UserId(principal);
            query = query.Where(item => item.OwnerUserId == userId);
        }

        var items = await query.ToArrayAsync(cancellationToken).ConfigureAwait(false);
        items = items.OrderBy(item => item.CreatedAt).ToArray();
        return TypedResults.Ok(items.Select(ToPortTunnelResponse).ToArray());
    }

    private static async Task<IResult> CreatePortTunnelDefinition(
        CreatePortTunnelDefinitionRequest request,
        ClaimsPrincipal principal,
        ProxyNetworkerDbContext db,
        CancellationToken cancellationToken)
    {
        var user = await CurrentUserAsync(principal, db, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var protocol = request.Protocol.Trim().ToLowerInvariant();
        if (protocol is not ("tcp" or "udp"))
        {
            return TypedResults.BadRequest(new ErrorResponse("Protocol must be tcp or udp."));
        }

        if (request.PrivatePort <= 0 || request.PrivatePort > 65535)
        {
            return TypedResults.BadRequest(new ErrorResponse("Private port must be between 1 and 65535."));
        }

        if (!AccountContext.IsAdmin(principal))
        {
            var existing = await db.PortTunnelDefinitions.CountAsync(item => item.OwnerUserId == user.Id, cancellationToken).ConfigureAwait(false);
            if (existing >= user.MaxPortTunnels)
            {
                return TypedResults.BadRequest(new ErrorResponse("Port Tunnel quota exceeded."));
            }
        }

        var publicPort = await AllocatePublicPortAsync(user, db, cancellationToken).ConfigureAwait(false);
        if (publicPort is null)
        {
            return TypedResults.BadRequest(new ErrorResponse("No public port is available in the assigned range."));
        }

        var record = new PortTunnelDefinitionRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            OwnerUserId = user.Id,
            Name = string.IsNullOrWhiteSpace(request.Name) ? $"tunnel-{publicPort}" : request.Name.Trim(),
            Protocol = protocol,
            PrivateHost = string.IsNullOrWhiteSpace(request.PrivateHost) ? "127.0.0.1" : request.PrivateHost.Trim(),
            PrivatePort = request.PrivatePort,
            PublicPort = publicPort.Value,
            BandwidthLimitBytes = user.BandwidthLimitBytes,
            MaxTrafficSpeedBytesPerSecond = user.MaxTrafficSpeedBytesPerSecond,
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.PortTunnelDefinitions.Add(record);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.Created($"/api/port-tunnels/{record.Id}", ToPortTunnelResponse(record));
    }

    private static async Task<IResult> DeletePortTunnelDefinition(
        string id,
        ClaimsPrincipal principal,
        ProxyNetworkerDbContext db,
        CancellationToken cancellationToken)
    {
        var record = await FindPortTunnelDefinitionAsync(id, principal, db, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return TypedResults.NotFound(new ErrorResponse("Port Tunnel was not found."));
        }

        var tokens = await db.AccessTokens
            .Where(item => item.ScopeKind == AccessTokenScopes.PortTunnel && item.ResourceId == id)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        db.AccessTokens.RemoveRange(tokens);
        db.PortTunnelDefinitions.Remove(record);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static Task<IResult> ListVirtualNetworkTokens(
        string id,
        ClaimsPrincipal principal,
        ProxyNetworkerDbContext db,
        CancellationToken cancellationToken)
    {
        return ListTokens(id, AccessTokenScopes.VirtualNetwork, principal, db, cancellationToken);
    }

    private static Task<IResult> CreateVirtualNetworkToken(
        string id,
        CreateAccessTokenRequest request,
        ClaimsPrincipal principal,
        ProxyNetworkerDbContext db,
        TokenGenerator tokenGenerator,
        CancellationToken cancellationToken)
    {
        return CreateToken(id, AccessTokenScopes.VirtualNetwork, "vnet", request, principal, db, tokenGenerator, cancellationToken);
    }

    private static Task<IResult> ListPortTunnelTokens(
        string id,
        ClaimsPrincipal principal,
        ProxyNetworkerDbContext db,
        CancellationToken cancellationToken)
    {
        return ListTokens(id, AccessTokenScopes.PortTunnel, principal, db, cancellationToken);
    }

    private static Task<IResult> CreatePortTunnelToken(
        string id,
        CreateAccessTokenRequest request,
        ClaimsPrincipal principal,
        ProxyNetworkerDbContext db,
        TokenGenerator tokenGenerator,
        CancellationToken cancellationToken)
    {
        return CreateToken(id, AccessTokenScopes.PortTunnel, "ptun", request, principal, db, tokenGenerator, cancellationToken);
    }

    private static async Task<IResult> ListTokens(
        string resourceId,
        string scope,
        ClaimsPrincipal principal,
        ProxyNetworkerDbContext db,
        CancellationToken cancellationToken)
    {
        if (!await CanAccessResourceAsync(resourceId, scope, principal, db, cancellationToken).ConfigureAwait(false))
        {
            return TypedResults.NotFound(new ErrorResponse("Resource was not found."));
        }

        var tokens = await db.AccessTokens
            .AsNoTracking()
            .Where(item => item.ResourceId == resourceId && item.ScopeKind == scope)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        tokens = tokens.OrderBy(item => item.CreatedAt).ToArray();

        return TypedResults.Ok(tokens.Select(ToAccessTokenResponse).ToArray());
    }

    private static async Task<IResult> CreateToken(
        string resourceId,
        string scope,
        string tokenPrefix,
        CreateAccessTokenRequest request,
        ClaimsPrincipal principal,
        ProxyNetworkerDbContext db,
        TokenGenerator tokenGenerator,
        CancellationToken cancellationToken)
    {
        if (!await CanAccessResourceAsync(resourceId, scope, principal, db, cancellationToken).ConfigureAwait(false))
        {
            return TypedResults.NotFound(new ErrorResponse("Resource was not found."));
        }

        var tokenType = NormalizeTokenType(request.TokenType);
        if (tokenType is null)
        {
            return TypedResults.BadRequest(new ErrorResponse("Token type must be Permanent, Limited or OneTime."));
        }

        if (scope == AccessTokenScopes.PortTunnel && tokenType == AccessTokenTypes.OneTime)
        {
            return TypedResults.BadRequest(new ErrorResponse("Port Tunnel token supports Permanent or Limited only."));
        }

        if (tokenType == AccessTokenTypes.Limited && request.ValidUntil is null)
        {
            return TypedResults.BadRequest(new ErrorResponse("Limited token requires ValidUntil."));
        }

        if (request.ValidFrom is not null && request.ValidUntil is not null && request.ValidUntil <= request.ValidFrom)
        {
            return TypedResults.BadRequest(new ErrorResponse("ValidUntil must be later than ValidFrom."));
        }

        var plainText = tokenGenerator.CreateToken(tokenPrefix);
        var record = new AccessTokenRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            ScopeKind = scope,
            ResourceId = resourceId,
            TokenHash = tokenGenerator.HashToken(plainText),
            TokenPreview = tokenGenerator.Preview(plainText),
            TokenType = tokenType,
            ValidFrom = request.ValidFrom,
            ValidUntil = request.ValidUntil,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = AccountContext.UserId(principal)
        };

        db.AccessTokens.Add(record);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.Created($"/api/{scope}/{resourceId}/tokens/{record.Id}", new CreatedAccessTokenResponse(ToAccessTokenResponse(record), plainText));
    }

    private static async Task<UserAccount?> CurrentUserAsync(
        ClaimsPrincipal principal,
        ProxyNetworkerDbContext db,
        CancellationToken cancellationToken)
    {
        var id = AccountContext.UserId(principal);
        return await db.Users.FirstOrDefaultAsync(item => item.Id == id, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int?> AllocatePublicPortAsync(
        UserAccount user,
        ProxyNetworkerDbContext db,
        CancellationToken cancellationToken)
    {
        var start = Math.Max(1, user.PortRangeStart);
        var end = Math.Min(65535, user.PortRangeEnd);
        if (start > end)
        {
            return null;
        }

        var used = await db.PortTunnelDefinitions
            .AsNoTracking()
            .Where(item => item.PublicPort >= start && item.PublicPort <= end)
            .Select(item => item.PublicPort)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        var usedSet = used.ToHashSet();
        var capacity = end - start + 1;

        if (usedSet.Count >= capacity)
        {
            return null;
        }

        var randomAttempts = Math.Min(capacity, 64);
        for (var index = 0; index < randomAttempts; index++)
        {
            var candidate = RandomNumberGenerator.GetInt32(start, end + 1);
            if (!usedSet.Contains(candidate))
            {
                return candidate;
            }
        }

        var offset = RandomNumberGenerator.GetInt32(0, capacity);
        for (var index = 0; index < capacity; index++)
        {
            var candidate = start + ((offset + index) % capacity);
            if (!usedSet.Contains(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static async Task<bool> CanAccessResourceAsync(
        string resourceId,
        string scope,
        ClaimsPrincipal principal,
        ProxyNetworkerDbContext db,
        CancellationToken cancellationToken)
    {
        if (AccountContext.IsAdmin(principal))
        {
            return scope switch
            {
                AccessTokenScopes.VirtualNetwork => await db.VirtualNetworks.AnyAsync(item => item.Id == resourceId, cancellationToken).ConfigureAwait(false),
                AccessTokenScopes.PortTunnel => await db.PortTunnelDefinitions.AnyAsync(item => item.Id == resourceId, cancellationToken).ConfigureAwait(false),
                _ => false
            };
        }

        var userId = AccountContext.UserId(principal);
        return scope switch
        {
            AccessTokenScopes.VirtualNetwork => await db.VirtualNetworks.AnyAsync(item => item.Id == resourceId && item.OwnerUserId == userId, cancellationToken).ConfigureAwait(false),
            AccessTokenScopes.PortTunnel => await db.PortTunnelDefinitions.AnyAsync(item => item.Id == resourceId && item.OwnerUserId == userId, cancellationToken).ConfigureAwait(false),
            _ => false
        };
    }

    private static Task<VirtualNetworkRecord?> FindVirtualNetworkAsync(
        string id,
        ClaimsPrincipal principal,
        ProxyNetworkerDbContext db,
        CancellationToken cancellationToken)
    {
        var query = db.VirtualNetworks.AsQueryable();
        if (!AccountContext.IsAdmin(principal))
        {
            var userId = AccountContext.UserId(principal);
            query = query.Where(item => item.OwnerUserId == userId);
        }

        return query.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
    }

    private static Task<PortTunnelDefinitionRecord?> FindPortTunnelDefinitionAsync(
        string id,
        ClaimsPrincipal principal,
        ProxyNetworkerDbContext db,
        CancellationToken cancellationToken)
    {
        var query = db.PortTunnelDefinitions.AsQueryable();
        if (!AccountContext.IsAdmin(principal))
        {
            var userId = AccountContext.UserId(principal);
            query = query.Where(item => item.OwnerUserId == userId);
        }

        return query.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
    }

    private static ErrorResponse? ValidateUserInputs(string username, string role, int portRangeStart, int portRangeEnd)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return new ErrorResponse("Username cannot be empty.");
        }

        if (role is not (UserRoles.Admin or UserRoles.User))
        {
            return new ErrorResponse("Role must be Admin or User.");
        }

        if (portRangeStart <= 0 || portRangeEnd > 65535 || portRangeStart > portRangeEnd)
        {
            return new ErrorResponse("Port range is invalid.");
        }

        return null;
    }

    private static string? NormalizeTokenType(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "permanent" => AccessTokenTypes.Permanent,
            "limited" => AccessTokenTypes.Limited,
            "onetime" or "one-time" => AccessTokenTypes.OneTime,
            _ => null
        };
    }

    private static AccountResponse ToAccountResponse(UserAccount user)
    {
        return new AccountResponse(
            user.Id,
            user.Username,
            user.Role,
            user.IsDisabled,
            user.MaxVirtualNetworks,
            user.MaxPortTunnels,
            user.PortRangeStart,
            user.PortRangeEnd,
            user.BandwidthLimitBytes,
            user.MaxTrafficSpeedBytesPerSecond,
            user.CreatedAt,
            user.PasswordChangedAt);
    }

    private static VirtualNetworkDefinitionResponse ToVirtualNetworkResponse(VirtualNetworkRecord record)
    {
        return new VirtualNetworkDefinitionResponse(
            record.Id,
            record.OwnerUserId,
            record.Name,
            record.GatewayAddress,
            record.PrefixLength,
            record.CreatedAt);
    }

    private static PortTunnelDefinitionResponse ToPortTunnelResponse(PortTunnelDefinitionRecord record)
    {
        return new PortTunnelDefinitionResponse(
            record.Id,
            record.OwnerUserId,
            record.Name,
            record.Protocol,
            record.PrivateHost,
            record.PrivatePort,
            record.PublicPort,
            record.BandwidthLimitBytes,
            record.MaxTrafficSpeedBytesPerSecond,
            record.CreatedAt);
    }

    private static AccessTokenResponse ToAccessTokenResponse(AccessTokenRecord record)
    {
        return new AccessTokenResponse(
            record.Id,
            record.ScopeKind,
            record.ResourceId,
            record.TokenPreview,
            record.TokenType,
            record.ValidFrom,
            record.ValidUntil,
            record.IsConsumed,
            record.CreatedAt);
    }
}
