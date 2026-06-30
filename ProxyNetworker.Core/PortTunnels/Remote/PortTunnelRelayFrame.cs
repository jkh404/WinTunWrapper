using System.Buffers.Binary;
using System.Text;

namespace ProxyNetworker.Core.PortTunnels.Remote;

public sealed class PortTunnelRelayFrame
{
    public const int HeaderLength = 13;
    public const int MaxPayloadLength = 1024 * 1024;

    public PortTunnelRelayFrame(PortTunnelRelayFrameType type, ulong connectionId, ReadOnlyMemory<byte> payload)
    {
        if (payload.Length > MaxPayloadLength)
        {
            throw new ArgumentOutOfRangeException(nameof(payload), payload.Length, $"Payload cannot exceed {MaxPayloadLength} bytes.");
        }

        Type = type;
        ConnectionId = connectionId;
        Payload = payload;
    }

    public PortTunnelRelayFrameType Type { get; }

    public ulong ConnectionId { get; }

    public ReadOnlyMemory<byte> Payload { get; }

    public static PortTunnelRelayFrame Text(PortTunnelRelayFrameType type, ulong connectionId, string text)
    {
        return new PortTunnelRelayFrame(type, connectionId, Encoding.UTF8.GetBytes(text));
    }

    public string GetTextPayload()
    {
        return Encoding.UTF8.GetString(Payload.Span);
    }

    public byte[] Encode()
    {
        var buffer = new byte[HeaderLength + Payload.Length];
        buffer[0] = (byte)Type;
        BinaryPrimitives.WriteUInt64BigEndian(buffer.AsSpan(1, 8), ConnectionId);
        BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(9, 4), Payload.Length);
        Payload.CopyTo(buffer.AsMemory(HeaderLength));
        return buffer;
    }

    public static PortTunnelRelayFrame Decode(ReadOnlyMemory<byte> buffer)
    {
        if (buffer.Length < HeaderLength)
        {
            throw new InvalidDataException("Relay frame is too short.");
        }

        var span = buffer.Span;
        var type = (PortTunnelRelayFrameType)span[0];
        var connectionId = BinaryPrimitives.ReadUInt64BigEndian(span.Slice(1, 8));
        var payloadLength = BinaryPrimitives.ReadInt32BigEndian(span.Slice(9, 4));
        if (payloadLength < 0 || payloadLength > MaxPayloadLength)
        {
            throw new InvalidDataException($"Relay frame payload length is invalid: {payloadLength}.");
        }

        if (buffer.Length != HeaderLength + payloadLength)
        {
            throw new InvalidDataException("Relay frame payload length does not match the message length.");
        }

        return new PortTunnelRelayFrame(type, connectionId, buffer.Slice(HeaderLength, payloadLength).ToArray());
    }
}
