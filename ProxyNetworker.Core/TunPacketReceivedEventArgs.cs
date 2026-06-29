namespace ProxyNetworker.Core;

public sealed class TunPacketReceivedEventArgs : EventArgs
{
    public TunPacketReceivedEventArgs(byte[] packet)
    {
        Packet = packet ?? throw new ArgumentNullException(nameof(packet));
    }

    public byte[] Packet { get; }

    public int Length => Packet.Length;
}
