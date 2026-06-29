using ProxyNetworker.Server.Management;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddSingleton<TunnelRegistry>();

var app = builder.Build();

app.UseExceptionHandler();

app.MapGet("/", () => TypedResults.Ok(new ServerInfoResponse(
    Name: "ProxyNetworker.Server",
    Role: "Management API",
    Capabilities: ["port-tunnel", "virtual-network"])));

app.MapGet("/health", () => TypedResults.Ok(new HealthResponse("healthy", DateTimeOffset.UtcNow)));

var tunnels = app.MapGroup("/api/tunnels");

tunnels.MapGet("/", (TunnelRegistry registry) => TypedResults.Ok(registry.List()));
tunnels.MapPost("/port", StartPortTunnel);
tunnels.MapPost("/virtual-network", StartVirtualNetwork);
tunnels.MapDelete("/{id}", StopTunnel);

await app.RunAsync();

static async Task<IResult> StartPortTunnel(
    CreatePortTunnelRequest request,
    TunnelRegistry registry,
    CancellationToken cancellationToken)
{
    try
    {
        var tunnel = await registry.StartPortTunnelAsync(request, cancellationToken);
        return TypedResults.Created($"/api/tunnels/{tunnel.Id}", tunnel);
    }
    catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
    {
        return TypedResults.BadRequest(new ErrorResponse(ex.Message));
    }
}

static async Task<IResult> StartVirtualNetwork(
    CreateVirtualNetworkRequest request,
    TunnelRegistry registry,
    CancellationToken cancellationToken)
{
    try
    {
        var tunnel = await registry.StartVirtualNetworkAsync(request, cancellationToken);
        return TypedResults.Created($"/api/tunnels/{tunnel.Id}", tunnel);
    }
    catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException or PlatformNotSupportedException)
    {
        return TypedResults.BadRequest(new ErrorResponse(ex.Message));
    }
}

static IResult StopTunnel(string id, TunnelRegistry registry)
{
    return registry.Stop(id)
        ? TypedResults.NoContent()
        : TypedResults.NotFound(new ErrorResponse($"Tunnel '{id}' was not found."));
}
