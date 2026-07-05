using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using ProxyNetworker.Server.Management;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProxyNetworker.Server.Data;
using ProxyNetworker.Server.PortTunnels;
using ProxyNetworker.Server.Security;
using ProxyNetworker.Server.VirtualNetworks;
using Serilog;
using Serilog.Events;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    var serverDataRoot = ResolveServerDataRoot(builder.Environment.ContentRootPath);
    var proxyNetworkerConnectionString = ResolveSqliteConnectionString(
        builder.Configuration.GetConnectionString("ProxyNetworker") ?? "Data Source=proxy-networker.db",
        serverDataRoot);
    var serverLogPath = Path.Combine(serverDataRoot, "logs", "server-.log");

    builder.Host.UseSerilog((context, services, loggerConfiguration) =>
    {
        ConfigureSerilog(
            loggerConfiguration,
            context.Configuration,
            services,
            "ProxyNetworker.Server",
            serverLogPath);
    });

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
        options.UseSqlite(proxyNetworkerConnectionString));
    builder.Services.AddDataProtection()
        .SetApplicationName("ProxyNetworker.Server")
        .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(serverDataRoot, "data-protection-keys")));
    builder.Services.AddOptions<SystemSettingsOptions>()
        .BindConfiguration(SystemSettingsOptions.Section)
        .ValidateDataAnnotations()
        .ValidateOnStart();
    builder.Services.AddSingleton<IValidateOptions<SystemSettingsOptions>, SystemSettingsOptionsValidator>();
    builder.Services.AddOptions<NetworkDefaultsOptions>()
        .BindConfiguration(NetworkDefaultsOptions.Section)
        .ValidateDataAnnotations()
        .ValidateOnStart();
    builder.Services.AddSingleton<IValidateOptions<NetworkDefaultsOptions>, NetworkDefaultsOptionsValidator>();
    builder.Services.AddHostedService<DatabaseInitializer>();
    builder.Services.AddSingleton<PasswordHasher>();
    builder.Services.AddSingleton<TokenGenerator>();
    builder.Services.AddSingleton<AccessTokenSecretProtector>();
    builder.Services.AddSingleton<TunnelRegistry>();
    builder.Services.AddSingleton<PortTunnelAccessService>();
    builder.Services.AddSingleton<RemotePortTunnelRegistry>();
    builder.Services.AddSingleton<VirtualNetworkAccessService>();
    builder.Services.AddSingleton<VirtualNetworkRuntimeRegistry>();

    var app = builder.Build();
    app.Logger.LogInformation("ProxyNetworker data root: {DataRoot}", serverDataRoot);
    app.Logger.LogInformation("ProxyNetworker SQLite database: {DataSource}", GetSqliteDataSource(proxyNetworkerConnectionString));

    app.UseExceptionHandler();
    app.UseDefaultFiles();
    app.UseStaticFiles();
    app.UseSerilogRequestLogging();
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

    tunnels.MapGet("/", async (
        TunnelRegistry registry,
        RemotePortTunnelRegistry remoteRegistry,
        VirtualNetworkRuntimeRegistry virtualNetworkRegistry,
        CancellationToken cancellationToken) =>
    {
        var local = await registry.ListAsync(cancellationToken);
        var remote = remoteRegistry.List();
        var virtualNetworks = virtualNetworkRegistry.List();
        return TypedResults.Ok(local.Concat(remote).Concat(virtualNetworks).OrderBy(item => item.StartedAt).ToArray());
    });
    tunnels.MapPost("/port", StartPortTunnel);
    tunnels.MapPost("/virtual-network", StartVirtualNetwork);
    tunnels.MapDelete("/{id}", StopTunnel);

    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "ProxyNetworker.Server terminated unexpectedly.");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

static void ConfigureSerilog(
    LoggerConfiguration loggerConfiguration,
    IConfiguration configuration,
    IServiceProvider services,
    string applicationName,
    string defaultLogPath)
{
    var logDirectory = Path.GetDirectoryName(defaultLogPath);
    if (!string.IsNullOrWhiteSpace(logDirectory))
    {
        Directory.CreateDirectory(logDirectory);
    }

    loggerConfiguration
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
        .MinimumLevel.Override("System", LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", applicationName)
        .WriteTo.Console()
        .WriteTo.File(
            defaultLogPath,
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 14,
            shared: true)
        .ReadFrom.Configuration(configuration)
        .ReadFrom.Services(services);
}

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

static string ResolveServerDataRoot(string contentRootPath)
{
    var contentRoot = Path.GetFullPath(contentRootPath);
    var directory = new DirectoryInfo(contentRoot);
    for (var current = directory; current is not null; current = current.Parent)
    {
        if (File.Exists(Path.Combine(current.FullName, "ProxyNetworker.Server.csproj")))
        {
            return current.FullName;
        }
    }

    return contentRoot;
}

static string ResolveSqliteConnectionString(string connectionString, string dataRoot)
{
    var connectionStringBuilder = new SqliteConnectionStringBuilder(connectionString);
    if (string.IsNullOrWhiteSpace(connectionStringBuilder.DataSource) ||
        connectionStringBuilder.DataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase) ||
        connectionStringBuilder.DataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ||
        Path.IsPathRooted(connectionStringBuilder.DataSource))
    {
        return connectionStringBuilder.ToString();
    }

    var dataSource = Path.GetFullPath(Path.Combine(dataRoot, connectionStringBuilder.DataSource));
    var dataSourceDirectory = Path.GetDirectoryName(dataSource);
    if (!string.IsNullOrWhiteSpace(dataSourceDirectory))
    {
        Directory.CreateDirectory(dataSourceDirectory);
    }

    connectionStringBuilder.DataSource = dataSource;
    return connectionStringBuilder.ToString();
}

static string GetSqliteDataSource(string connectionString)
{
    return new SqliteConnectionStringBuilder(connectionString).DataSource;
}
