using System.Buffers;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using static Vanara.PInvoke.IpHlpApi;
using static Vanara.PInvoke.Ws2_32;

namespace ProxyNetworker.Core.Platforms.Windows;

[SupportedOSPlatform("windows")]
public sealed class WintunAdapterWrapper : IDisposable
{
    public delegate void WintunLoggerCallBack(WintunLoggerLevel loggerLevel, DateTime dateTime, string? message);
    public delegate void ReceiveCallBack(Span<byte> data);

    private readonly WintunNative.WintunLoggerCallback _loggerCallback;
    private IntPtr _adapterPtr = IntPtr.Zero;
    private IntPtr _sessionPtr = IntPtr.Zero;
    private bool _isQuit;
    private bool _disposed;
    private Thread? _receiveThread;

    private WintunAdapterWrapper(Guid? requestedGuid, IntPtr adapterPtr, string name, string tunnelType, bool isOpen)
    {
        if (adapterPtr == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create or open Wintun adapter.");
        }

        ID = requestedGuid;
        _adapterPtr = adapterPtr;
        Name = name;
        TunnelType = tunnelType;
        IsOpen = isOpen;
        _loggerCallback = DefaultWintunLoggerCallBack;
        WintunNative.WintunSetLogger(Marshal.GetFunctionPointerForDelegate(_loggerCallback));
    }

    public WintunLoggerCallBack? OnLog { get; set; }

    public ReceiveCallBack? OnReceive { get; set; }

    public Guid? ID { get; }

    public string Name { get; }

    public string TunnelType { get; }

    public bool IsOpen { get; private set; }

    public bool IsStart { get; private set; }

    public uint SessionCapacity { get; set; } = 1024 * 1024;

    public void Open()
    {
        ThrowIfDisposed();
        if (IsOpen && _adapterPtr != IntPtr.Zero)
        {
            return;
        }

        _adapterPtr = WintunNative.WintunOpenAdapter(Name);
        if (_adapterPtr == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Failed to open Wintun adapter '{Name}'.");
        }

        IsOpen = true;
    }

    public void Start(IPAddress ipAddress, uint? sessionCapacity = null, byte onLinkPrefixLength = 24)
    {
        ThrowIfDisposed();
        if (IsOpen)
        {
            StartSession(ipAddress, sessionCapacity ?? SessionCapacity, onLinkPrefixLength);
        }

        ReceivePacket();
    }

    public void StartAsync(IPAddress ipAddress, uint? sessionCapacity = null, byte onLinkPrefixLength = 24)
    {
        ThrowIfDisposed();
        if (IsOpen)
        {
            StartSession(ipAddress, sessionCapacity ?? SessionCapacity, onLinkPrefixLength);
        }

        _receiveThread = new Thread(ReceivePacket)
        {
            IsBackground = true,
            Name = $"wintun-{Name}-receive"
        };
        _receiveThread.Start();
    }

    public void Close()
    {
        ThrowIfDisposed();
        if (_adapterPtr == IntPtr.Zero)
        {
            throw new InvalidOperationException("Wintun adapter is not open.");
        }

        EndSession();
        WintunNative.WintunCloseAdapter(_adapterPtr);
        _adapterPtr = IntPtr.Zero;
        IsOpen = false;
    }

    public ulong GetLUID()
    {
        ThrowIfDisposed();
        if (_adapterPtr == IntPtr.Zero)
        {
            throw new InvalidOperationException("Wintun adapter is not open.");
        }

        WintunNative.WintunGetAdapterLUID(_adapterPtr, out WintunNative.NetLuidLh luid);
        return luid.Value;
    }

    public void SendPacket(byte[] packetData)
    {
        ThrowIfDisposed();
        if (packetData is null)
        {
            throw new ArgumentNullException(nameof(packetData));
        }

        if (packetData.Length == 0)
        {
            throw new ArgumentException("Packet cannot be empty.", nameof(packetData));
        }

        if (_sessionPtr == IntPtr.Zero)
        {
            throw new InvalidOperationException("Wintun session has not been started.");
        }

        if (packetData.Length > WintunNative.WintunMaxIpPacketSize)
        {
            throw new ArgumentOutOfRangeException(nameof(packetData), packetData.Length, "Packet exceeds Wintun maximum IP packet size.");
        }

        var dataPtr = WintunNative.WintunAllocateSendPacket(_sessionPtr, (uint)packetData.Length);
        if (dataPtr == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "WintunAllocateSendPacket failed.");
        }

        Marshal.Copy(packetData, 0, dataPtr, packetData.Length);
        WintunNative.WintunSendPacket(_sessionPtr, dataPtr);
    }

    public int GetRunningDriverVersion()
    {
        ThrowIfDisposed();
        if (_adapterPtr == IntPtr.Zero)
        {
            throw new InvalidOperationException("Wintun adapter is not open.");
        }

        var version = WintunNative.WintunGetRunningDriverVersion();
        if (version == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to get Wintun running driver version.");
        }

        return version;
    }

    public static WintunAdapterWrapper Create(string name = "Default", string tunnelType = "Default", Guid? requestedGuid = null)
    {
        requestedGuid ??= Guid.NewGuid();
        var guidPtr = WintunNative.ToNativeGuidPointer(requestedGuid.Value);
        try
        {
            var adapterPtr = WintunNative.WintunCreateAdapter(name, tunnelType, guidPtr);
            return new WintunAdapterWrapper(requestedGuid, adapterPtr, name, tunnelType, true);
        }
        finally
        {
            Marshal.FreeHGlobal(guidPtr);
        }
    }

    public static bool DeleteDriver()
    {
        return WintunNative.WintunDeleteDriver() != 0;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void ReceivePacket()
    {
        using var waitHandle = new EventWaitHandle(false, EventResetMode.AutoReset);
        var readWaitEvent = WintunNative.WintunGetReadWaitEvent(_sessionPtr);
        waitHandle.SafeWaitHandle = new SafeWaitHandle(readWaitEvent, ownsHandle: false);

        while (!_isQuit)
        {
            while (IsOpen && IsStart && _sessionPtr != IntPtr.Zero && !_isQuit)
            {
                try
                {
                    var dataPtr = WintunNative.WintunReceivePacket(_sessionPtr, out var packetDataSize);
                    if (packetDataSize > 0 && dataPtr != IntPtr.Zero)
                    {
                        var data = ArrayPool<byte>.Shared.Rent(checked((int)packetDataSize));
                        try
                        {
                            Marshal.Copy(dataPtr, data, 0, checked((int)packetDataSize));
                            OnReceive?.Invoke(data.AsSpan(0, checked((int)packetDataSize)));
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(data);
                            WintunNative.WintunReleaseReceivePacket(_sessionPtr, dataPtr);
                        }
                    }
                    else if (Marshal.GetLastWin32Error() == WintunNative.ErrorNoMoreItems)
                    {
                        waitHandle.WaitOne();
                    }
                }
                catch (ObjectDisposedException) when (_isQuit)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
            }

            Thread.Sleep(1000);
        }
    }

    private bool StartSession(IPAddress ipAddress, uint capacity, byte onLinkPrefixLength)
    {
        if (_adapterPtr == IntPtr.Zero)
        {
            throw new InvalidOperationException("Wintun adapter is not open.");
        }

        if (!IsOpen)
        {
            throw new InvalidOperationException("Wintun adapter is not open.");
        }

        if (ipAddress.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            throw new PlatformNotSupportedException("WintunAdapterWrapper currently configures IPv4 addresses only.");
        }

        if (capacity < WintunNative.WintunMinRingCapacity ||
            capacity > WintunNative.WintunMaxRingCapacity ||
            (capacity & (capacity - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be a power of two between 128 KiB and 64 MiB.");
        }

        MIB_UNICASTIPADDRESS_ROW addressRow;
        InitializeUnicastIpAddressEntry(out addressRow);
        WintunNative.WintunGetAdapterLUID(_adapterPtr, out WintunNative.NetLuidLh luid);
        addressRow.InterfaceLuid = new NET_LUID
        {
            Value = luid.Value
        };
        addressRow.Address.Ipv4.sin_family = ADDRESS_FAMILY.AF_INET;
        addressRow.Address.Ipv4.sin_addr = new IN_ADDR(ipAddress.GetAddressBytes());
        addressRow.OnLinkPrefixLength = onLinkPrefixLength;
        addressRow.DadState = NL_DAD_STATE.IpDadStatePreferred;
        CreateUnicastIpAddressEntry(ref addressRow);

        _sessionPtr = WintunNative.WintunStartSession(_adapterPtr, capacity);
        if (_sessionPtr == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to start Wintun session.");
        }

        IsStart = true;
        return true;
    }

    private void EndSession()
    {
        if (!IsStart || _sessionPtr == IntPtr.Zero)
        {
            return;
        }

        WintunNative.WintunEndSession(_sessionPtr);
        _sessionPtr = IntPtr.Zero;
        IsStart = false;
    }

    private void DefaultWintunLoggerCallBack(WintunLoggerLevel loggerLevel, long timestamp, IntPtr message)
    {
        var eventTime = new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(timestamp);
        var text = message != IntPtr.Zero ? Marshal.PtrToStringUni(message) : null;
        OnLog?.Invoke(loggerLevel, eventTime.ToLocalTime(), text);
    }

    private void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _isQuit = true;
        }

        _disposed = true;
        if (_sessionPtr != IntPtr.Zero)
        {
            IsStart = false;
            WintunNative.WintunEndSession(_sessionPtr);
            _sessionPtr = IntPtr.Zero;
        }

        if (_adapterPtr != IntPtr.Zero)
        {
            IsOpen = false;
            WintunNative.WintunCloseAdapter(_adapterPtr);
            _adapterPtr = IntPtr.Zero;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(WintunAdapterWrapper));
        }
    }

    ~WintunAdapterWrapper()
    {
        Dispose(false);
    }
}
