using System.Net;

namespace ProxyNetworker.Core;

public class VirtualNetworkTunnelOptions
{
    public IPEndPoint BindEndPoint { get; set; } = new(IPAddress.Any, 0);

    public IPEndPoint? RemoteEndPoint { get; set; }

    public IPAddress NodeAddress { get; set; } = IPAddress.Parse("10.66.0.1");

    public int PrefixLength { get; set; } = 24;

    public string NodeName { get; set; } = Environment.MachineName;

    public int MaxPacketSize { get; set; } = 65535;

    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan PeerTimeout { get; set; } = TimeSpan.FromSeconds(20);

    public void Validate()
    {
        if (BindEndPoint is null)
        {
            throw new ArgumentNullException(nameof(BindEndPoint));
        }

        if (MaxPacketSize <= 0 || MaxPacketSize > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxPacketSize), MaxPacketSize, "UDP packet size must be between 1 and 65535.");
        }

        if (NodeAddress.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            throw new PlatformNotSupportedException("Virtual Network currently supports IPv4 only.");
        }

        if (PrefixLength < 0 || PrefixLength > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(PrefixLength), PrefixLength, "IPv4 prefix length must be between 0 and 32.");
        }

        if (HeartbeatInterval < TimeSpan.FromSeconds(1))
        {
            throw new ArgumentOutOfRangeException(nameof(HeartbeatInterval), HeartbeatInterval, "Heartbeat interval must be at least 1 second.");
        }

        if (PeerTimeout <= HeartbeatInterval)
        {
            throw new ArgumentOutOfRangeException(nameof(PeerTimeout), PeerTimeout, "Peer timeout must be greater than the heartbeat interval.");
        }
    }
}
