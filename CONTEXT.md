# ProxyNetworker

ProxyNetworker connects private networks and services across machines that cannot directly reach each other.

## Language

**Port Tunnel**:
A tunnel that exposes one TCP or UDP endpoint and forwards traffic to one private service endpoint.
_Avoid_: Port forwarding, proxy rule

**Virtual Network**:
A layer-3 network formed between ProxyNetworker nodes, where IP packets move through TUN devices.
_Avoid_: VPN, virtual LAN

**Node**:
A running ProxyNetworker process that participates in a Port Tunnel or Virtual Network.
_Avoid_: Host, machine, client

**Peer**:
The remote Node currently exchanging tunnel traffic with this Node.
_Avoid_: Remote, endpoint

**Private Service**:
A TCP or UDP service reachable from a ProxyNetworker Client but not directly reachable from the public network.
_Avoid_: Upstream, target

**Public Endpoint**:
The externally reachable TCP or UDP endpoint exposed by a ProxyNetworker Server.
_Avoid_: Listen port, external port
