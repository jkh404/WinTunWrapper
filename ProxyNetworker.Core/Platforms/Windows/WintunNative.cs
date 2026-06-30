using System.Runtime.InteropServices;
using System.Text;
using static Vanara.PInvoke.IpHlpApi;

namespace ProxyNetworker.Core.Platforms.Windows;

internal static class WintunNative
{
    public const int WintunMinRingCapacity = 0x20000;
    public const int WintunMaxRingCapacity = 0x4000000;
    public const int WintunMaxIpPacketSize = 0xFFFF;
    public const long ErrorNoMoreItems = 259L;
    private const string WintunDll = "wintun.dll";

    public delegate void WintunLoggerCallback(WintunLoggerLevel loggerLevel, long timestamp, IntPtr message);

    [DllImport(WintunDll, EntryPoint = "WintunCreateAdapter", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr WintunCreateAdapter(string name, string tunnelType, IntPtr requestedGuid);

    [DllImport(WintunDll, EntryPoint = "WintunOpenAdapter", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr WintunOpenAdapter(string name);

    [DllImport(WintunDll, EntryPoint = "WintunCloseAdapter")]
    public static extern void WintunCloseAdapter(IntPtr adapter);

    [DllImport(WintunDll, EntryPoint = "WintunDeleteDriver", SetLastError = true)]
    public static extern int WintunDeleteDriver();

    [DllImport(WintunDll, EntryPoint = "WintunGetAdapterLUID")]
    public static extern void WintunGetAdapterLUID(IntPtr adapter, out NET_LUID luid);

    [DllImport(WintunDll, EntryPoint = "WintunGetAdapterLUID")]
    public static extern void WintunGetAdapterLUID(IntPtr adapter, out NetLuidLh luid);

    [DllImport(WintunDll, EntryPoint = "WintunGetRunningDriverVersion", SetLastError = true)]
    public static extern int WintunGetRunningDriverVersion();

    [DllImport(WintunDll, EntryPoint = "WintunSetLogger")]
    public static extern int WintunSetLogger(WintunLoggerCallback callback);

    [DllImport(WintunDll, EntryPoint = "WintunStartSession", SetLastError = true)]
    public static extern IntPtr WintunStartSession(IntPtr adapter, uint capacity);

    [DllImport(WintunDll, EntryPoint = "WintunEndSession")]
    public static extern void WintunEndSession(IntPtr session);

    [DllImport(WintunDll, EntryPoint = "WintunGetReadWaitEvent")]
    public static extern IntPtr WintunGetReadWaitEvent(IntPtr session);

    [DllImport(WintunDll, EntryPoint = "WintunReceivePacket", SetLastError = true)]
    public static extern IntPtr WintunReceivePacket(IntPtr session, out uint packetSize);

    [DllImport(WintunDll, EntryPoint = "WintunReleaseReceivePacket")]
    public static extern void WintunReleaseReceivePacket(IntPtr session, IntPtr packet);

    [DllImport(WintunDll, EntryPoint = "WintunAllocateSendPacket", SetLastError = true)]
    public static extern IntPtr WintunAllocateSendPacket(IntPtr session, uint packetSize);

    [DllImport(WintunDll, EntryPoint = "WintunSendPacket")]
    public static extern void WintunSendPacket(IntPtr session, IntPtr packet);

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

    [StructLayout(LayoutKind.Explicit, Size = 8)]
    public struct NetLuidLh
    {
        [FieldOffset(0)]
        public ulong Value;

        [FieldOffset(0)]
        public InfoStruct Info;

        [StructLayout(LayoutKind.Explicit, Size = 8)]
        public struct InfoStruct
        {
            [FieldOffset(0)] public ushort IfType;
            [FieldOffset(3)] public Int3Byte NetLuidIndex;
            [FieldOffset(5)] public Int3Byte Reserved;
        }
    }

    [StructLayout(LayoutKind.Explicit, Size = 3)]
    public struct Int3Byte
    {
        [FieldOffset(0)] public byte B0;
        [FieldOffset(1)] public byte B1;
        [FieldOffset(2)] public byte B2;

        public int ToInt()
        {
            return B0 | (B1 << 8) | (B2 << 16);
        }

        public override string ToString()
        {
            return ToInt().ToString();
        }
    }
}
