using WintunWrapper;
using System.Net.Sockets;

namespace ProxyNetworker.Core.Platforms.Windows;

internal sealed class WintunTunDevice : ITunDevice
{
    private readonly TunDeviceOptions _options;
    private readonly Action<ProxyNetworkerLogEntry>? _logger;
    private WintunAdapterWrapper? _adapter;
    private int _started;

    public WintunTunDevice(TunDeviceOptions options, Action<ProxyNetworkerLogEntry>? logger)
    {
        _options = options;
        _logger = logger;
        Name = options.Name;
    }

    public string Name { get; private set; }

    public bool IsStarted => _started == 1;

    public event EventHandler<TunPacketReceivedEventArgs>? PacketReceived;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_options.Address.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new PlatformNotSupportedException("The Windows Wintun backend currently configures IPv4 addresses only.");
        }

        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            return Task.CompletedTask;
        }

        try
        {
            _adapter = WintunAdapterWrapper.Create(_options.Name, _options.TunnelType, _options.WindowsAdapterId);
            _adapter.OnLog = OnWintunLog;
            _adapter.OnReceive = OnReceive;
            _adapter.Open();
            _adapter.StartAsync(_options.Address, _options.WindowsSessionCapacity, checked((byte)_options.PrefixLength));

            Log(ProxyNetworkerLogLevel.Information, $"Started Wintun adapter '{Name}' with {_options.Address}/{_options.PrefixLength}.");
            return Task.CompletedTask;
        }
        catch
        {
            Interlocked.Exchange(ref _started, 0);
            _adapter?.Dispose();
            _adapter = null;
            throw;
        }
    }

    public ValueTask SendAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (packet.IsEmpty)
        {
            throw new ArgumentException("Packet cannot be empty.", nameof(packet));
        }

        var adapter = _adapter ?? throw new InvalidOperationException("The Wintun adapter has not been started.");
        adapter.SendPacket(packet.ToArray());
        return default;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _started, 0) == 0)
        {
            return;
        }

        _adapter?.Dispose();
        _adapter = null;
    }

    private void OnReceive(Span<byte> packet)
    {
        PacketReceived?.Invoke(this, new TunPacketReceivedEventArgs(packet.ToArray()));
    }

    private void OnWintunLog(WintunLoggerLevel level, DateTime timestamp, string? message)
    {
        var mappedLevel = level switch
        {
            WintunLoggerLevel.ERR => ProxyNetworkerLogLevel.Error,
            WintunLoggerLevel.WARN => ProxyNetworkerLogLevel.Warning,
            _ => ProxyNetworkerLogLevel.Information
        };

        _logger?.Invoke(new ProxyNetworkerLogEntry(
            mappedLevel,
            message ?? string.Empty,
            timestamp: new DateTimeOffset(timestamp)));
    }

    private void Log(ProxyNetworkerLogLevel level, string message)
    {
        _logger?.Invoke(new ProxyNetworkerLogEntry(level, message));
    }
}
