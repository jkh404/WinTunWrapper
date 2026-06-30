using System.Runtime.InteropServices;
using System.Text;

namespace ProxyNetworker.Core.Platforms.Windows;

internal static partial class WintunNative
{
    public const int WintunMinRingCapacity = 0x20000;
    public const int WintunMaxRingCapacity = 0x4000000;
    public const int WintunMaxIpPacketSize = 0xFFFF;
    public const long ErrorNoMoreItems = 259L;
    private const string WintunDll = "wintun.dll";

    public delegate void WintunLoggerCallback(WintunLoggerLevel loggerLevel, long timestamp, IntPtr message);

    [LibraryImport(WintunDll, EntryPoint = "WintunCreateAdapter", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    public static partial IntPtr WintunCreateAdapter(string name, string tunnelType, IntPtr requestedGuid);

    [LibraryImport(WintunDll, EntryPoint = "WintunOpenAdapter", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    public static partial IntPtr WintunOpenAdapter(string name);

    [LibraryImport(WintunDll, EntryPoint = "WintunCloseAdapter")]
    public static partial void WintunCloseAdapter(IntPtr adapter);

    [LibraryImport(WintunDll, EntryPoint = "WintunDeleteDriver", SetLastError = true)]
    public static partial int WintunDeleteDriver();

    [LibraryImport(WintunDll, EntryPoint = "WintunGetAdapterLUID")]
    public static partial void WintunGetAdapterLUID(IntPtr adapter, out NetLuidLh luid);

    [LibraryImport(WintunDll, EntryPoint = "WintunGetRunningDriverVersion", SetLastError = true)]
    public static partial int WintunGetRunningDriverVersion();

    [LibraryImport(WintunDll, EntryPoint = "WintunSetLogger")]
    public static partial int WintunSetLogger(IntPtr callback);

    [LibraryImport(WintunDll, EntryPoint = "WintunStartSession", SetLastError = true)]
    public static partial IntPtr WintunStartSession(IntPtr adapter, uint capacity);

    [LibraryImport(WintunDll, EntryPoint = "WintunEndSession")]
    public static partial void WintunEndSession(IntPtr session);

    [LibraryImport(WintunDll, EntryPoint = "WintunGetReadWaitEvent")]
    public static partial IntPtr WintunGetReadWaitEvent(IntPtr session);

    [LibraryImport(WintunDll, EntryPoint = "WintunReceivePacket", SetLastError = true)]
    public static partial IntPtr WintunReceivePacket(IntPtr session, out uint packetSize);

    [LibraryImport(WintunDll, EntryPoint = "WintunReleaseReceivePacket")]
    public static partial void WintunReleaseReceivePacket(IntPtr session, IntPtr packet);

    [LibraryImport(WintunDll, EntryPoint = "WintunAllocateSendPacket", SetLastError = true)]
    public static partial IntPtr WintunAllocateSendPacket(IntPtr session, uint packetSize);

    [LibraryImport(WintunDll, EntryPoint = "WintunSendPacket")]
    public static partial void WintunSendPacket(IntPtr session, IntPtr packet);

    public static IntPtr ToNativeGuidPointer(Guid value)
    {
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<Guid>());
        Marshal.StructureToPtr(value, pointer, false);
        return pointer;
    }

    public static IntPtr ToNativeStringPointer(string value, Encoding? encoding = null)
    {
        encoding ??= Encoding.Unicode;
        var data = encoding.GetBytes(value);
        var pointer = Marshal.AllocHGlobal(data.Length + 1);
        Marshal.Copy(data, 0, pointer, data.Length);
        Marshal.WriteByte(pointer, data.Length, 0);
        return pointer;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NetLuidLh
    {
        public ulong Value;
    }
}
