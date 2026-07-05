using System.Text.Json;

internal sealed class ProxyNetworkerClientConfig
{
    public const string AppSettingsSectionName = "ProxyNetworkerClient";

    public string? ApiServer { get; set; }

    public string? Server { get; set; }

    public List<RemotePortTunnelClientConfig> PortTunnels { get; set; } = [];

    public DirectVirtualNetworkClientConfig? VirtualNetwork { get; set; }

    public static async Task<ProxyNetworkerClientConfig> LoadAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
        {
            return new ProxyNetworkerClientConfig();
        }

        if (TryGetPropertyIgnoreCase(root, AppSettingsSectionName, out var appSettingsSection))
        {
            return DeserializeConfig(appSettingsSection);
        }

        return DeserializeConfig(root);
    }

    public static async Task WriteSampleAsync(string path, CancellationToken cancellationToken)
    {
        var sample = CreateSampleJson();
        object output;
        if (IsAppSettingsPath(path))
        {
            output = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [AppSettingsSectionName] = sample,
                ["Serilog"] = CreateSerilogSampleJson()
            };
        }
        else
        {
            sample["Serilog"] = CreateSerilogSampleJson();
            output = sample;
        }

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, output, JsonOptions, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync("\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
    }

    public string? GetDefaultServer()
    {
        return ApiServer ?? Server;
    }

    private static ProxyNetworkerClientConfig DeserializeConfig(JsonElement element)
    {
        return element.Deserialize<ProxyNetworkerClientConfig>(JsonOptions) ?? new ProxyNetworkerClientConfig();
    }

    private static Dictionary<string, object> CreateSampleJson()
    {
        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["apiServer"] = "http://127.0.0.1:12301",
            ["portTunnels"] = new[]
            {
                new
                {
                    enabled = false,
                    name = "web",
                    token = "ptun_replace_this_token"
                }
            },
            ["virtualNetwork"] = new
            {
                enabled = false,
                name = "pn-client",
                token = "vnet_replace_this_token"
            }
        };
    }

    private static object CreateSerilogSampleJson()
    {
        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["MinimumLevel"] = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["Default"] = "Information",
                ["Override"] = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["Microsoft"] = "Warning",
                    ["System"] = "Warning"
                }
            }
        };
    }

    private static bool IsAppSettingsPath(string path)
    {
        var fileName = Path.GetFileName(path);
        return fileName.Equals("appsettings.json", StringComparison.OrdinalIgnoreCase) ||
            (fileName.StartsWith("appsettings.", StringComparison.OrdinalIgnoreCase) &&
             fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.NameEquals(propertyName) || string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static JsonSerializerOptions JsonOptions => new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
}

internal sealed class RemotePortTunnelClientConfig
{
    public bool Enabled { get; set; } = true;

    public string? Name { get; set; }

    public string? Server { get; set; }

    public string Token { get; set; } = string.Empty;
}

internal sealed class DirectVirtualNetworkClientConfig
{
    public bool Enabled { get; set; }

    public string? Server { get; set; }

    public string? Token { get; set; }

    public string Name { get; set; } = "pn-client";

    public string ServerHost { get; set; } = "127.0.0.1";

    public int ServerPort { get; set; } = 51820;

    public int BindPort { get; set; }

    public string TunAddress { get; set; } = "10.66.0.2";

    public int PrefixLength { get; set; } = 24;

    public int Mtu { get; set; } = 1400;
}
