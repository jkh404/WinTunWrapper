namespace ProxyNetworker.Core.PortTunnels.Remote;

public enum PortTunnelRelayFrameType : byte
{
    Ready = 1,
    TcpOpen = 2,
    TcpData = 3,
    TcpClose = 4,
    UdpDatagram = 5,
    Error = 6,
    Ping = 7
}
