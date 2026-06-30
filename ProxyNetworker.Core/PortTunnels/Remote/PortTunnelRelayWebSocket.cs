using System.Net.WebSockets;

namespace ProxyNetworker.Core.PortTunnels.Remote;

public static class PortTunnelRelayWebSocket
{
    public static async Task SendAsync(WebSocket socket, PortTunnelRelayFrame frame, SemaphoreSlim sendLock, CancellationToken cancellationToken)
    {
        if (socket.State != WebSocketState.Open)
        {
            throw new WebSocketException("Relay WebSocket is not open.");
        }

        var buffer = frame.Encode();
        await sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await socket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Binary, true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            sendLock.Release();
        }
    }

    public static async Task<PortTunnelRelayFrame?> ReceiveAsync(
        WebSocket socket,
        byte[] buffer,
        MemoryStream messageBuffer,
        CancellationToken cancellationToken)
    {
        messageBuffer.SetLength(0);

        while (true)
        {
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            if (result.MessageType != WebSocketMessageType.Binary)
            {
                throw new InvalidDataException("Relay WebSocket messages must be binary.");
            }

            messageBuffer.Write(buffer, 0, result.Count);
            if (messageBuffer.Length > PortTunnelRelayFrame.HeaderLength + PortTunnelRelayFrame.MaxPayloadLength)
            {
                throw new InvalidDataException("Relay WebSocket message is too large.");
            }

            if (result.EndOfMessage)
            {
                return PortTunnelRelayFrame.Decode(messageBuffer.ToArray());
            }
        }
    }
}
