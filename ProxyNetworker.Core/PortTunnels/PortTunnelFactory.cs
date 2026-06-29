namespace ProxyNetworker.Core.PortTunnels;

public static class PortTunnelFactory
{
    public static IPortTunnel Create(PortTunnelRule rule, Action<ProxyNetworkerLogEntry>? logger = null)
    {
        if (rule is null)
        {
            throw new ArgumentNullException(nameof(rule));
        }

        rule.Validate();

        return rule.Protocol switch
        {
            PortTunnelProtocol.Tcp => new TcpPortTunnel(rule, logger),
            PortTunnelProtocol.Udp => new UdpPortTunnel(rule, logger),
            _ => throw new ArgumentOutOfRangeException(nameof(rule.Protocol), rule.Protocol, "Unsupported port tunnel protocol.")
        };
    }
}
