# ProxyNetworker

ProxyNetworker connects private networks and services across machines that cannot directly reach each other.

## Language

**Port Tunnel**:
A tunnel that exposes one TCP or UDP endpoint and forwards traffic to one private service endpoint.
_Avoid_: Port forwarding, proxy rule

**Virtual Network**:
A layer-3 network formed between ProxyNetworker nodes, where IP packets move through TUN devices.
_Avoid_: VPN, virtual LAN

**LAN Discovery**:
Broadcast or multicast traffic used by games and local applications to find peers inside a Virtual Network.
_Avoid_: Service discovery, broadcast magic

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

**Account**:
A person who signs in to the management console and owns Virtual Networks or Port Tunnels.
_Avoid_: User when referring to a process or node

**Role**:
The permission level attached to an Account. Administrators manage every Account and quota; regular users manage only their own resources inside assigned limits.
_Avoid_: Group

**Quota**:
The per-Account limits that constrain how many Virtual Networks and Port Tunnels can be created and which Public Endpoint ports may be allocated.
_Avoid_: Plan, package

**Access Token**:
A token issued for a Virtual Network or Port Tunnel so an end user can connect a client Node without signing in to the management console.
_Avoid_: Login token, API key
