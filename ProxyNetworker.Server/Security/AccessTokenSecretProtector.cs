using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace ProxyNetworker.Server.Security;

public sealed class AccessTokenSecretProtector
{
    private const string Purpose = "ProxyNetworker.Server.AccessTokens.v1";
    private readonly IDataProtector _protector;

    public AccessTokenSecretProtector(IDataProtectionProvider dataProtectionProvider)
    {
        _protector = dataProtectionProvider.CreateProtector(Purpose);
    }

    public string Protect(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("Token cannot be empty.", nameof(token));
        }

        return _protector.Protect(token);
    }

    public string? TryUnprotect(string? protectedToken)
    {
        if (string.IsNullOrWhiteSpace(protectedToken))
        {
            return null;
        }

        try
        {
            return _protector.Unprotect(protectedToken);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
