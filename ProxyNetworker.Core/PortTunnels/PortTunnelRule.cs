using System.Net;

namespace ProxyNetworker.Core.PortTunnels;

public sealed class PortTunnelRule
{
    public string Name { get; set; } = "default";

    public PortTunnelProtocol Protocol { get; set; } = PortTunnelProtocol.Tcp;

    public IPEndPoint PublicEndpoint { get; set; } = new(IPAddress.Loopback, 0);

    public HostEndPoint PrivateService { get; set; } = new("127.0.0.1", 1);

    public int BufferSize { get; set; } = 64 * 1024;

    public TimeSpan UdpSessionIdleTimeout { get; set; } = TimeSpan.FromMinutes(2);

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new ArgumentException("Port tunnel name cannot be empty.", nameof(Name));
        }

        if (PublicEndpoint is null)
        {
            throw new ArgumentNullException(nameof(PublicEndpoint));
        }

        if (PrivateService is null)
        {
            throw new ArgumentNullException(nameof(PrivateService));
        }

        if (BufferSize < 1024 || BufferSize > 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(BufferSize), BufferSize, "Buffer size must be between 1 KiB and 1 MiB.");
        }

        if (UdpSessionIdleTimeout < TimeSpan.FromSeconds(5))
        {
            throw new ArgumentOutOfRangeException(nameof(UdpSessionIdleTimeout), UdpSessionIdleTimeout, "UDP session idle timeout must be at least 5 seconds.");
        }
    }
}
