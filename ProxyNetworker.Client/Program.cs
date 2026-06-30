using System.Net;
using System.Net.Sockets;
using ProxyNetworker.Core;
using ProxyNetworker.Core.PortTunnels;

if (IsMode(args, "port-tunnel"))
{
    return await RunPortTunnelAsync(args.Skip(1).ToArray());
}

if (IsMode(args, "virtual-network"))
{
    return await RunVirtualNetworkAsync(args.Skip(1).ToArray());
}

return await RunVirtualNetworkAsync(args);

static async Task<int> RunVirtualNetworkAsync(string[] args)
{
var serverHost = GetOption(args, "--server") ?? GetPositional(args, 0);
if (string.IsNullOrWhiteSpace(serverHost))
{
    PrintUsage();
    return 2;
}

var serverPort = GetIntOption(args, "--port", GetPositional(args, 1), 51820);
var bindPort = GetIntOption(args, "--bind-port", null, 0);
var tunAddress = IPAddress.Parse(GetOption(args, "--tun-address") ?? "10.66.0.2");
var prefixLength = GetIntOption(args, "--prefix", null, 24);
var mtu = GetIntOption(args, "--mtu", null, 1400);
var name = GetOption(args, "--name") ?? "pn-client";
var remoteAddress = await ResolveAddressAsync(serverHost);
var remoteEndPoint = new IPEndPoint(remoteAddress, serverPort);

using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};

using var tunDevice = TunDeviceFactory.Create(new TunDeviceOptions
{
    Name = name,
    TunnelType = "ProxyNetworker",
    Address = tunAddress,
    PrefixLength = prefixLength,
    Mtu = mtu
}, WriteLog);

using var tunnel = new VirtualNetworkTunnel(
    tunDevice,
    new VirtualNetworkTunnelOptions
    {
        BindEndPoint = new IPEndPoint(IPAddress.Any, bindPort),
        RemoteEndPoint = remoteEndPoint,
        NodeAddress = tunAddress,
        PrefixLength = prefixLength,
        NodeName = name
    },
    WriteLog);

await tunnel.StartAsync(shutdown.Token);
Console.WriteLine($"ProxyNetworker Virtual Network client started. TUN={name} {tunAddress}/{prefixLength}, peer={remoteEndPoint}.");
Console.WriteLine("Press Ctrl+C to stop.");

try
{
    await Task.Delay(Timeout.InfiniteTimeSpan, shutdown.Token);
}
catch (OperationCanceledException)
{
}

return 0;
}

static async Task<int> RunPortTunnelAsync(string[] args)
{
    if (GetOption(args, "--server") is not null || GetOption(args, "--token") is not null)
    {
        return await RunRemotePortTunnelAsync(args);
    }

    var protocol = ParseProtocol(GetOption(args, "--protocol") ?? "tcp");
    var listen = ParseIPEndPoint(GetOption(args, "--listen") ?? "127.0.0.1:8080");
    var targetValue = GetOption(args, "--target");
    if (string.IsNullOrWhiteSpace(targetValue))
    {
        PrintUsage();
        return 2;
    }

    var target = ParseHostEndPoint(targetValue);
    var name = GetOption(args, "--name") ?? "pn-port-tunnel";

    using var shutdown = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        shutdown.Cancel();
    };

    using var tunnel = PortTunnelFactory.Create(new PortTunnelRule
    {
        Name = name,
        Protocol = protocol,
        PublicEndpoint = listen,
        PrivateService = target
    }, WriteLog);

    await tunnel.StartAsync(shutdown.Token);
    Console.WriteLine($"ProxyNetworker Port Tunnel started. protocol={protocol}, public={listen}, private={target}.");
    Console.WriteLine("Press Ctrl+C to stop.");

    try
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, shutdown.Token);
    }
    catch (OperationCanceledException)
    {
    }

    return 0;
}

static async Task<int> RunRemotePortTunnelAsync(string[] args)
{
    var server = GetOption(args, "--server") ?? GetPositional(args, 0);
    var token = GetOption(args, "--token");
    if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(token))
    {
        PrintUsage();
        return 2;
    }

    var endpoint = BuildPortTunnelWebSocketEndpoint(server);

    using var shutdown = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        shutdown.Cancel();
    };

    using var client = new RemotePortTunnelClient(endpoint, token, WriteLog);
    Console.WriteLine($"ProxyNetworker remote Port Tunnel connecting to {endpoint}.");
    Console.WriteLine("Press Ctrl+C to stop.");

    await client.RunAsync(shutdown.Token);
    return 0;
}

static async Task<IPAddress> ResolveAddressAsync(string host)
{
    if (IPAddress.TryParse(host, out var address))
    {
        return address;
    }

    var addresses = await Dns.GetHostAddressesAsync(host);
    return addresses.First(static item => item.AddressFamily == AddressFamily.InterNetwork);
}

static string? GetOption(string[] args, string name)
{
    for (var i = 0; i < args.Length; i++)
    {
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
        {
            return args[i + 1];
        }

        var prefix = name + "=";
        if (args[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return args[i][prefix.Length..];
        }
    }

    return null;
}

static string? GetPositional(string[] args, int index)
{
    return args.Where(static item => !item.StartsWith("--", StringComparison.Ordinal)).Skip(index).FirstOrDefault();
}

static int GetIntOption(string[] args, string name, string? fallbackValue, int defaultValue)
{
    var value = GetOption(args, name) ?? fallbackValue;
    return int.TryParse(value, out var parsed) ? parsed : defaultValue;
}

static bool IsMode(string[] args, string mode)
{
    return args.Length > 0 && string.Equals(args[0], mode, StringComparison.OrdinalIgnoreCase);
}

static PortTunnelProtocol ParseProtocol(string value)
{
    return value.ToLowerInvariant() switch
    {
        "tcp" => PortTunnelProtocol.Tcp,
        "udp" => PortTunnelProtocol.Udp,
        _ => throw new ArgumentException($"Unsupported protocol '{value}'. Use tcp or udp.")
    };
}

static IPEndPoint ParseIPEndPoint(string value)
{
    var (host, port) = SplitHostPort(value);
    if (!IPAddress.TryParse(host, out var address))
    {
        throw new ArgumentException($"Listen endpoint host must be an IP address: '{value}'.");
    }

    return new IPEndPoint(address, port);
}

static HostEndPoint ParseHostEndPoint(string value)
{
    var (host, port) = SplitHostPort(value);
    return new HostEndPoint(host, port);
}

static (string Host, int Port) SplitHostPort(string value)
{
    var index = value.LastIndexOf(':');
    if (index <= 0 || index == value.Length - 1)
    {
        throw new ArgumentException($"Endpoint must be formatted as host:port: '{value}'.");
    }

    var host = value[..index];
    var portText = value[(index + 1)..];
    if (!int.TryParse(portText, out var port) || port <= 0 || port > 65535)
    {
        throw new ArgumentException($"Endpoint port must be between 1 and 65535: '{value}'.");
    }

    return (host, port);
}

static Uri BuildPortTunnelWebSocketEndpoint(string server)
{
    var value = server.Contains("://", StringComparison.Ordinal)
        ? server
        : $"http://{server}";
    var baseUri = new Uri(value.EndsWith("/", StringComparison.Ordinal) ? value : $"{value}/");
    var endpoint = new Uri(baseUri, "api/client/port-tunnels/connect");
    var builder = new UriBuilder(endpoint)
    {
        Scheme = baseUri.Scheme.ToLowerInvariant() switch
        {
            "https" => "wss",
            "wss" => "wss",
            _ => "ws"
        }
    };

    return builder.Uri;
}

static void WriteLog(ProxyNetworkerLogEntry entry)
{
    Console.WriteLine($"[{entry.Timestamp:O}] {entry.Level}: {entry.Message}");
    if (entry.Exception is not null)
    {
        Console.WriteLine(entry.Exception);
    }
}

static void PrintUsage()
{
    Console.WriteLine("Virtual Network: ProxyNetworker.Client --server <host> [--port 51820] [--tun-address 10.66.0.2] [--prefix 24] [--name pn-client]");
    Console.WriteLine("Remote Tunnel:   ProxyNetworker.Client port-tunnel --server http://server:5000 --token ptun_xxx");
    Console.WriteLine("Local Tunnel:    ProxyNetworker.Client port-tunnel --protocol tcp|udp --listen 127.0.0.1:8080 --target 127.0.0.1:80 [--name web]");
}
