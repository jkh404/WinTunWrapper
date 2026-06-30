using System.Security.Cryptography;
using System.Text;

namespace ProxyNetworker.Server.Security;

public sealed class TokenGenerator
{
    public string CreateToken(string prefix)
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return $"{prefix}_{Base64UrlEncode(bytes)}";
    }

    public string HashToken(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public string Preview(string token)
    {
        return token.Length <= 12 ? token : $"{token[..8]}...{token[^4..]}";
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
