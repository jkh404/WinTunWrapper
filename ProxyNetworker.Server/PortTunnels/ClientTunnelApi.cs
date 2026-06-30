using System.Net.WebSockets;
using ProxyNetworker.Server.Management;
using ProxyNetworker.Server.VirtualNetworks;

namespace ProxyNetworker.Server.PortTunnels;

public static class ClientTunnelApi
{
    public static IEndpointRouteBuilder MapClientTunnelApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/client/port-tunnels/connect", ConnectPortTunnelClient).AllowAnonymous();
        app.MapGet("/api/client/virtual-networks/config", GetVirtualNetworkClientConfig).AllowAnonymous();
        return app;
    }

    private static async Task ConnectPortTunnelClient(
        HttpContext httpContext,
        PortTunnelAccessService accessService,
        RemotePortTunnelRegistry relayRegistry,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("ProxyNetworker.Server.PortTunnelClient");

        if (!httpContext.WebSockets.IsWebSocketRequest)
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await httpContext.Response.WriteAsync("WebSocket request is required.", httpContext.RequestAborted).ConfigureAwait(false);
            return;
        }

        var token = ReadToken(httpContext.Request);
        var grant = await accessService.ValidatePortTunnelTokenAsync(token, httpContext.RequestAborted).ConfigureAwait(false);
        if (grant is null)
        {
            httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        using var webSocket = await httpContext.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
        try
        {
            await relayRegistry.RunClientAsync(grant.Definition, webSocket, httpContext.RequestAborted).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Rejected duplicate port tunnel client for {TunnelName}.", grant.Definition.Name);
            await CloseWebSocketAsync(webSocket, WebSocketCloseStatus.PolicyViolation, ex.Message, httpContext.RequestAborted).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
        {
        }
        catch (WebSocketException ex)
        {
            logger.LogInformation(ex, "Port tunnel client WebSocket closed for {TunnelName}.", grant.Definition.Name);
        }
    }

    private static string ReadToken(HttpRequest request)
    {
        if (request.Headers.TryGetValue("X-ProxyNetworker-Token", out var headerToken))
        {
            return headerToken.ToString();
        }

        return request.Query.TryGetValue("token", out var queryToken)
            ? queryToken.ToString()
            : string.Empty;
    }

    private static async Task<IResult> GetVirtualNetworkClientConfig(
        HttpContext httpContext,
        VirtualNetworkAccessService accessService,
        VirtualNetworkRuntimeRegistry runtimeRegistry)
    {
        var token = ReadToken(httpContext.Request);
        var grant = await accessService.ValidateVirtualNetworkTokenAsync(token, httpContext.RequestAborted).ConfigureAwait(false);
        if (grant is null)
        {
            return TypedResults.Unauthorized();
        }

        try
        {
            await runtimeRegistry.EnsureRunningAsync(grant.Definition, httpContext.RequestAborted).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or PlatformNotSupportedException or UnauthorizedAccessException)
        {
            return TypedResults.BadRequest(new ErrorResponse(ex.Message));
        }

        return TypedResults.Ok(new VirtualNetworkClientConfigResponse(
            grant.Definition.Id,
            grant.Definition.Name,
            ResolveServerHost(httpContext.Request),
            grant.Definition.ListenPort,
            grant.Definition.GatewayAddress,
            grant.ClientAddress.ToString(),
            grant.Definition.PrefixLength,
            grant.Definition.Mtu));
    }

    private static string ResolveServerHost(HttpRequest request)
    {
        if (!request.Headers.TryGetValue("X-Forwarded-Host", out var forwardedHost) ||
            string.IsNullOrWhiteSpace(forwardedHost.ToString()))
        {
            return request.Host.Host;
        }

        var firstHost = forwardedHost.ToString().Split(',')[0].Trim();
        return HostString.FromUriComponent(firstHost).Host;
    }

    private static async Task CloseWebSocketAsync(WebSocket webSocket, WebSocketCloseStatus status, string description, CancellationToken cancellationToken)
    {
        if (webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            await webSocket.CloseAsync(status, description, cancellationToken).ConfigureAwait(false);
        }
    }
}
