namespace ProxyNetworker.Core;

public interface ITunDevice : IDisposable
{
    string Name { get; }

    bool IsStarted { get; }

    event EventHandler<TunPacketReceivedEventArgs>? PacketReceived;

    Task StartAsync(CancellationToken cancellationToken = default);

    ValueTask SendAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken = default);
}
