using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ProxyNetworker.Core.Platforms.Linux;

internal sealed partial class LinuxTunDevice : ITunDevice
{
    private const int O_RDWR = 2;
    private const short IFF_TUN = 0x0001;
    private const short IFF_NO_PI = 0x1000;
    private const ulong TUNSETIFF = 0x400454ca;
    private const int IFNAMSIZ = 16;
    private const int MaxPacketSize = 65535;

    private readonly TunDeviceOptions _options;
    private readonly Action<ProxyNetworkerLogEntry>? _logger;
    private FileStream? _stream;
    private CancellationTokenSource? _readLoopCancellation;
    private Task? _readLoop;
    private int _started;

    public LinuxTunDevice(TunDeviceOptions options, Action<ProxyNetworkerLogEntry>? logger)
    {
        _options = options;
        _logger = logger;
        Name = options.Name;
    }

    public string Name { get; private set; }

    public bool IsStarted => _started == 1;

    public event EventHandler<TunPacketReceivedEventArgs>? PacketReceived;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_options.Address.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new PlatformNotSupportedException("The Linux TUN backend currently configures IPv4 addresses only.");
        }

        ValidateLinuxInterfaceName(_options.Name);

        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            return;
        }

        var fd = open("/dev/net/tun", O_RDWR);
        if (fd < 0)
        {
            Interlocked.Exchange(ref _started, 0);
            throw new InvalidOperationException($"Failed to open /dev/net/tun. errno={Marshal.GetLastWin32Error()}.");
        }

        try
        {
            var ifr = IfReq.Create(_options.Name, IFF_TUN | IFF_NO_PI);

            if (ioctl(fd, TUNSETIFF, ref ifr) < 0)
            {
                throw new InvalidOperationException($"Failed to create Linux TUN device '{_options.Name}'. errno={Marshal.GetLastWin32Error()}.");
            }

            Name = ifr.GetName();
            var handle = new SafeFileHandle(new IntPtr(fd), ownsHandle: true);
            fd = -1;
            _stream = new FileStream(handle, FileAccess.ReadWrite, MaxPacketSize, isAsync: true);

            if (_options.ConfigureInterface)
            {
                await ConfigureInterfaceAsync(cancellationToken).ConfigureAwait(false);
            }

            _readLoopCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _readLoop = Task.Run(() => ReadLoopAsync(_readLoopCancellation.Token));

            Log(ProxyNetworkerLogLevel.Information, $"Started Linux TUN device '{Name}' with {_options.Address}/{_options.PrefixLength}.");
        }
        catch
        {
            Interlocked.Exchange(ref _started, 0);
            _stream?.Dispose();
            _stream = null;

            if (fd >= 0)
            {
                close(fd);
            }

            throw;
        }
    }

    public async ValueTask SendAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (packet.IsEmpty)
        {
            throw new ArgumentException("Packet cannot be empty.", nameof(packet));
        }

        var stream = _stream ?? throw new InvalidOperationException("The Linux TUN device has not been started.");
        var buffer = packet.ToArray();
        await stream.WriteAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _started, 0) == 0)
        {
            return;
        }

        _readLoopCancellation?.Cancel();
        _stream?.Dispose();

        try
        {
            _readLoop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(static item => item is OperationCanceledException or ObjectDisposedException))
        {
        }

        _readLoopCancellation?.Dispose();
        _readLoopCancellation = null;
        _stream = null;
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[MaxPacketSize];

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var stream = _stream;
                if (stream is null)
                {
                    return;
                }

                var length = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                if (length <= 0)
                {
                    continue;
                }

                var packet = new byte[length];
                Buffer.BlockCopy(buffer, 0, packet, 0, length);
                PacketReceived?.Invoke(this, new TunPacketReceivedEventArgs(packet));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Log(ProxyNetworkerLogLevel.Error, "Linux TUN read loop failed.", ex);
                return;
            }
        }
    }

    private async Task ConfigureInterfaceAsync(CancellationToken cancellationToken)
    {
        await RunCommandAsync("ip", $"addr replace {_options.Address}/{_options.PrefixLength} dev {Name}", cancellationToken).ConfigureAwait(false);
        await RunCommandAsync("ip", $"link set dev {Name} mtu {_options.Mtu} up", cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateLinuxInterfaceName(string name)
    {
        if (name.Length >= IFNAMSIZ)
        {
            throw new ArgumentException($"Linux interface names must be shorter than {IFNAMSIZ} bytes.", nameof(name));
        }

        if (name.Any(static c => !(char.IsLetterOrDigit(c) || c is '_' or '-' or '.')))
        {
            throw new ArgumentException("Linux interface names can only contain letters, digits, '_', '-' and '.'.", nameof(name));
        }
    }

    private async Task RunCommandAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start '{fileName}'.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await Task.Run(() => process.WaitForExit(), cancellationToken).ConfigureAwait(false);
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Command '{fileName} {arguments}' failed with exit code {process.ExitCode}. {stderr}{stdout}");
        }
    }

    private void Log(ProxyNetworkerLogLevel level, string message, Exception? exception = null)
    {
        _logger?.Invoke(new ProxyNetworkerLogEntry(level, message, exception));
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct IfReq
    {
        public fixed byte Name[IFNAMSIZ];

        public short Flags;

        public fixed byte Padding[22];

        public static IfReq Create(string name, short flags)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(name);
            if (bytes.Length >= IFNAMSIZ)
            {
                throw new ArgumentException($"Linux interface names must be shorter than {IFNAMSIZ} bytes.", nameof(name));
            }

            var request = new IfReq
            {
                Flags = flags
            };

            for (var index = 0; index < bytes.Length; index++)
            {
                request.Name[index] = bytes[index];
            }

            return request;
        }

        public string GetName()
        {
            var length = 0;
            while (length < IFNAMSIZ && Name[length] != 0)
            {
                length++;
            }

            var bytes = new byte[length];
            for (var index = 0; index < length; index++)
            {
                bytes[index] = Name[index];
            }

            return System.Text.Encoding.UTF8.GetString(bytes);
        }
    }

    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int open(string pathname, int flags);

    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static partial int ioctl(int fd, ulong request, ref IfReq ifr);

    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    private static partial int close(int fd);
}
