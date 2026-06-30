using System.Buffers.Binary;
using System.Net;

namespace ProxyNetworker.Core;

internal enum VirtualNetworkFrameType : byte
{
    Hello = 1,
    Packet = 2,
    Heartbeat = 3
}

internal readonly struct VirtualNetworkFrame
{
    private const uint Magic = 0x504e564e;
    private const byte Version = 1;
    private const int HeaderSize = 16;

    public VirtualNetworkFrame(VirtualNetworkFrameType type, IPAddress virtualAddress, ReadOnlyMemory<byte> payload)
    {
        Type = type;
        VirtualAddress = virtualAddress;
        Payload = payload;
    }

    public VirtualNetworkFrameType Type { get; }

    public IPAddress VirtualAddress { get; }

    public ReadOnlyMemory<byte> Payload { get; }

    public static byte[] Create(VirtualNetworkFrameType type, IPAddress virtualAddress, ReadOnlySpan<byte> payload)
    {
        var addressBytes = virtualAddress.GetAddressBytes();
        if (addressBytes.Length != 4)
        {
            throw new PlatformNotSupportedException("Virtual Network frames currently support IPv4 only.");
        }

        var frame = new byte[HeaderSize + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(0, 4), Magic);
        frame[4] = Version;
        frame[5] = (byte)type;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(6, 2), 0);
        addressBytes.CopyTo(frame.AsSpan(8, 4));
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(12, 4), checked((uint)payload.Length));
        payload.CopyTo(frame.AsSpan(HeaderSize));
        return frame;
    }

    public static bool TryParse(byte[] datagram, out VirtualNetworkFrame frame)
    {
        frame = default;

        if (datagram.Length < HeaderSize)
        {
            return false;
        }

        if (BinaryPrimitives.ReadUInt32BigEndian(datagram.AsSpan(0, 4)) != Magic || datagram[4] != Version)
        {
            return false;
        }

        var type = (VirtualNetworkFrameType)datagram[5];
        if (type is not (VirtualNetworkFrameType.Hello or VirtualNetworkFrameType.Packet or VirtualNetworkFrameType.Heartbeat))
        {
            return false;
        }

        var payloadLength = BinaryPrimitives.ReadUInt32BigEndian(datagram.AsSpan(12, 4));
        if (payloadLength != datagram.Length - HeaderSize)
        {
            return false;
        }

        var addressBytes = new byte[4];
        Buffer.BlockCopy(datagram, 8, addressBytes, 0, addressBytes.Length);

        frame = new VirtualNetworkFrame(
            type,
            new IPAddress(addressBytes),
            datagram.AsMemory(HeaderSize));
        return true;
    }
}
