using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;

Log.Logger = CreateLogger();

try
{
    return await ProxyNetworkerClientApp.RunAsync(CommandLine.Parse(args));
}
catch (Exception ex)
{
    Log.Fatal(ex, "ProxyNetworker.Client terminated unexpectedly.");
    Console.Error.WriteLine(ex.Message);
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

static Serilog.ILogger CreateLogger()
{
    var logPath = Path.Combine(AppContext.BaseDirectory, "logs", "client-.log");
    var logDirectory = Path.GetDirectoryName(logPath);
    if (!string.IsNullOrWhiteSpace(logDirectory))
    {
        Directory.CreateDirectory(logDirectory);
    }

    return new LoggerConfiguration()
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
        .MinimumLevel.Override("System", LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "ProxyNetworker.Client")
        .WriteTo.Console()
        .WriteTo.File(
            logPath,
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 14,
            shared: true)
        .ReadFrom.Configuration(BuildLoggingConfiguration())
        .CreateLogger();
}

static IConfiguration BuildLoggingConfiguration()
{
    var builder = new ConfigurationBuilder();
    AddJsonFileIfExists(builder, Path.Combine(AppContext.BaseDirectory, "appsettings.json"));
    AddJsonFileIfExists(builder, Path.Combine(AppContext.BaseDirectory, "proxynetworker.client.json"));

    var currentDirectory = Directory.GetCurrentDirectory();
    if (!Path.GetFullPath(currentDirectory).Equals(Path.GetFullPath(AppContext.BaseDirectory), StringComparison.OrdinalIgnoreCase))
    {
        AddJsonFileIfExists(builder, Path.Combine(currentDirectory, "appsettings.json"));
        AddJsonFileIfExists(builder, Path.Combine(currentDirectory, "proxynetworker.client.json"));
    }

    return builder.Build();
}

static void AddJsonFileIfExists(IConfigurationBuilder builder, string path)
{
    if (File.Exists(path))
    {
        builder.AddJsonFile(path, optional: false, reloadOnChange: false);
    }
}
