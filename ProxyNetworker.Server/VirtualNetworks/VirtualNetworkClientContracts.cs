namespace ProxyNetworker.Server.VirtualNetworks;

public sealed record VirtualNetworkClientConfigResponse(
    string NetworkId,
    string Name,
    string ServerHost,
    int ServerPort,
    string GatewayAddress,
    string ClientAddress,
    int PrefixLength,
    int Mtu);
