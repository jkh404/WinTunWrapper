namespace ProxyNetworker.Core.PortTunnels;

public interface IPortTunnel : IDisposable
{
    string Name { get; }

    PortTunnelProtocol Protocol { get; }

    bool IsStarted { get; }

    Task StartAsync(CancellationToken cancellationToken = default);
}
