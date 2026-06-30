using System.Security.Claims;
using ProxyNetworker.Server.Data;

namespace ProxyNetworker.Server.Management;

public static class AccountContext
{
    public static string UserId(ClaimsPrincipal user)
    {
        return user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Signed-in account id is missing.");
    }

    public static string Username(ClaimsPrincipal user)
    {
        return user.Identity?.Name ?? string.Empty;
    }

    public static bool IsAdmin(ClaimsPrincipal user)
    {
        return user.IsInRole(UserRoles.Admin);
    }
}
