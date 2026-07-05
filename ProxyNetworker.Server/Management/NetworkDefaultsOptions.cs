using System.ComponentModel.DataAnnotations;
using System.Net;
using Microsoft.Extensions.Options;

namespace ProxyNetworker.Server.Management;

public sealed class NetworkDefaultsOptions
{
    public const string Section = "NetworkDefaults";

    [Required]
    public string PortTunnelListenAddress { get; init; } = string.Empty;

    [Range(1, 65535)]
    public int PortTunnelListenPort { get; init; }

    [Required]
    public string VirtualNetworkGatewayAddress { get; init; } = string.Empty;

    [Range(1, 65535)]
    public int VirtualNetworkListenPort { get; init; }

    [Range(1, 30)]
    public int VirtualNetworkPrefixLength { get; init; }

    [Range(576, 65535)]
    public int VirtualNetworkMtu { get; init; }
}

public sealed class NetworkDefaultsOptionsValidator : IValidateOptions<NetworkDefaultsOptions>
{
    public ValidateOptionsResult Validate(string? name, NetworkDefaultsOptions options)
    {
        if (!IPAddress.TryParse(options.PortTunnelListenAddress, out _))
        {
            return ValidateOptionsResult.Fail("NetworkDefaults:PortTunnelListenAddress must be a valid IP address.");
        }

        if (!IPAddress.TryParse(options.VirtualNetworkGatewayAddress, out var gatewayAddress) ||
            gatewayAddress.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return ValidateOptionsResult.Fail("NetworkDefaults:VirtualNetworkGatewayAddress must be a valid IPv4 address.");
        }

        return ValidateOptionsResult.Success;
    }
}
