# ProxyNetworker.Server Docker 部署端口说明

## 端口类型

服务端有两类对外端口：

- 管理/API 端口：由 `urls` 配置控制，例如 `http://*:12301`，需要发布 TCP。
- 隧道/虚拟局域网端口：由资源创建时的 `ListenPort` 或系统端口范围分配，端口会在运行时绑定，需要提前发布到宿主机。

Docker bridge 网络不会因为容器内进程运行后动态绑定新端口，就自动开放宿主机端口。因此动态分配端口必须限制到固定范围，并在启动容器时预发布。

## 推荐配置

`appsettings.Docker.json` 已把动态端口范围限制为：

```json
{
  "SystemSettings": {
    "PublicPortRangeStart": 30010,
    "PublicPortRangeEnd": 30100
  }
}
```

同时保留两个直接启动隧道 API 的默认端口：

```json
{
  "NetworkDefaults": {
    "PortTunnelListenPort": 30000,
    "VirtualNetworkListenPort": 30001
  }
}
```

## docker run 示例

```powershell
docker run -d --name proxynetworker-server `
  -e ASPNETCORE_ENVIRONMENT=Docker `
  -e DOTNET_ENVIRONMENT=Docker `
  -p 12301:12301/tcp `
  -p 30000:30000/tcp `
  -p 30000:30000/udp `
  -p 30001:30001/udp `
  -p 30010-30100:30010-30100/tcp `
  -p 30010-30100:30010-30100/udp `
  your-image
```

如果只使用虚拟局域网，至少需要发布：

```powershell
-p 12301:12301/tcp `
-p 30010-30100:30010-30100/udp
```

## 已有数据库注意事项

`SystemSettings` 会持久化到 SQLite。已有数据库不会因为换了 `appsettings.Docker.json` 自动改端口范围。部署到 Docker 前，需要在管理页或 API 中把系统外网端口范围改到已经发布的范围，例如 `30010-30100`。

Linux 服务器也可以使用 `--network host` 避免端口发布范围问题，但这会让容器直接共享宿主网络命名空间，隔离性更弱。
