using Microsoft.AspNetCore.Authentication.Cookies;
using ProxyNetworker.Server.Management;
using Microsoft.EntityFrameworkCore;
using ProxyNetworker.Server.Data;
using ProxyNetworker.Server.PortTunnels;
using ProxyNetworker.Server.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
        options.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole(UserRoles.Admin));
});
builder.Services.AddDbContext<ProxyNetworkerDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("ProxyNetworker")
        ?? "Data Source=proxy-networker.db"));
builder.Services.AddHostedService<DatabaseInitializer>();
builder.Services.AddSingleton<PasswordHasher>();
builder.Services.AddSingleton<TokenGenerator>();
builder.Services.AddSingleton<TunnelRegistry>();
builder.Services.AddSingleton<PortTunnelAccessService>();
builder.Services.AddSingleton<RemotePortTunnelRegistry>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseWebSockets();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/info", () => TypedResults.Ok(new ServerInfoResponse(
    Name: "ProxyNetworker.Server",
    Role: "Management API",
    Capabilities: ["port-tunnel", "virtual-network"])));

app.MapGet("/health", () => TypedResults.Ok(new HealthResponse("healthy", DateTimeOffset.UtcNow)));

app.MapManagementApi();
app.MapClientTunnelApi();

var tunnels = app.MapGroup("/api/tunnels").RequireAuthorization("AdminOnly");

tunnels.MapGet("/", async (TunnelRegistry registry, RemotePortTunnelRegistry remoteRegistry, CancellationToken cancellationToken) =>
{
    var local = await registry.ListAsync(cancellationToken);
    var remote = remoteRegistry.List();
    return TypedResults.Ok(local.Concat(remote).OrderBy(item => item.StartedAt).ToArray());
});
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

static async Task<IResult> StopTunnel(string id, TunnelRegistry registry, CancellationToken cancellationToken)
{
    return await registry.StopAsync(id, cancellationToken)
        ? TypedResults.NoContent()
        : TypedResults.NotFound(new ErrorResponse($"Tunnel '{id}' was not found."));
}
