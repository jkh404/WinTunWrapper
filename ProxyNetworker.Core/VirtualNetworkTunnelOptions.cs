using System.Net;

namespace ProxyNetworker.Core;

public class VirtualNetworkTunnelOptions
{
    public IPEndPoint BindEndPoint { get; set; } = new(IPAddress.Any, 0);

    public IPEndPoint? RemoteEndPoint { get; set; }

    public int MaxPacketSize { get; set; } = 65535;

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
    }
}
