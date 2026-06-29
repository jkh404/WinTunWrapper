using System.Net;

namespace ProxyNetworker.Core;

public sealed class TunDeviceOptions
{
    public string Name { get; set; } = "ProxyNetworker";

    public string TunnelType { get; set; } = "ProxyNetworker";

    public IPAddress Address { get; set; } = IPAddress.Parse("10.66.0.2");

    public int PrefixLength { get; set; } = 24;

    public int Mtu { get; set; } = 1400;

    public Guid? WindowsAdapterId { get; set; }

    public uint WindowsSessionCapacity { get; set; } = 1u << 20;

    public bool ConfigureInterface { get; set; } = true;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new ArgumentException("TUN device name cannot be empty.", nameof(Name));
        }

        if (string.IsNullOrWhiteSpace(TunnelType))
        {
            throw new ArgumentException("Tunnel type cannot be empty.", nameof(TunnelType));
        }

        var maxPrefix = Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? 128 : 32;
        if (PrefixLength < 0 || PrefixLength > maxPrefix)
        {
            throw new ArgumentOutOfRangeException(nameof(PrefixLength), PrefixLength, $"Prefix length must be between 0 and {maxPrefix}.");
        }

        if (Mtu < 576 || Mtu > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(Mtu), Mtu, "MTU must be between 576 and 65535.");
        }
    }
}
