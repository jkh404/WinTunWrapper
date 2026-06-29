using System.Net;
using System.Net.Sockets;

namespace ProxyNetworker.Core.PortTunnels;

public sealed class HostEndPoint
{
    public HostEndPoint(string host, int port)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new ArgumentException("Host cannot be empty.", nameof(host));
        }

        if (port <= 0 || port > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), port, "Port must be between 1 and 65535.");
        }

        Host = host;
        Port = port;
    }

    public string Host { get; }

    public int Port { get; }

    public async Task<IPEndPoint> ResolveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (IPAddress.TryParse(Host, out var address))
        {
            return new IPEndPoint(address, Port);
        }

        var addresses = await Dns.GetHostAddressesAsync(Host).ConfigureAwait(false);
        var resolved = addresses.FirstOrDefault(static item => item.AddressFamily == AddressFamily.InterNetwork)
            ?? addresses.FirstOrDefault(static item => item.AddressFamily == AddressFamily.InterNetworkV6)
            ?? throw new InvalidOperationException($"No IP address was found for host '{Host}'.");

        return new IPEndPoint(resolved, Port);
    }

    public override string ToString()
    {
        return $"{Host}:{Port}";
    }
}
