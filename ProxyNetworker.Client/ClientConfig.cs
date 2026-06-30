using System.Text.Json;

internal sealed class ProxyNetworkerClientConfig
{
    public const string AppSettingsSectionName = "ProxyNetworkerClient";

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
        var sample = CreateSample();
        var output = IsAppSettingsPath(path)
            ? new Dictionary<string, ProxyNetworkerClientConfig>(StringComparer.Ordinal)
            {
                [AppSettingsSectionName] = sample
            }
            : (object)sample;

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, output, JsonOptions, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync("\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
    }

    private static ProxyNetworkerClientConfig DeserializeConfig(JsonElement element)
    {
        return element.Deserialize<ProxyNetworkerClientConfig>(JsonOptions) ?? new ProxyNetworkerClientConfig();
    }

    private static ProxyNetworkerClientConfig CreateSample()
    {
        return new ProxyNetworkerClientConfig
        {
            Server = "http://127.0.0.1:5000",
            PortTunnels =
            [
                new RemotePortTunnelClientConfig
                {
                    Name = "web",
                    Token = "ptun_replace_this_token"
                }
            ],
            VirtualNetwork = new DirectVirtualNetworkClientConfig
            {
                Enabled = false,
                Name = "pn-client",
                Server = "http://127.0.0.1:5000",
                Token = "vnet_replace_this_token",
                ServerHost = "127.0.0.1",
                ServerPort = 51820,
                TunAddress = "10.66.0.2",
                PrefixLength = 24,
                Mtu = 1400
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
