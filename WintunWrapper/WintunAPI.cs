using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.VisualBasic;

namespace WintunWrapper
{
    public static class Const
    {
        /// <summary>
        /// 最大适配器名称长度
        /// </summary>
        public const int MAX_ADAPTER_NAME = 128;
        /// <summary>
        /// 环形缓冲区最小长度. 128kiB
        /// </summary>
        public const int WINTUN_MIN_RING_CAPACITY = 0x20000;
        /// <summary>
        /// 环形缓冲区最大长度. 64MiB
        /// </summary>
        public const int WINTUN_MAX_RING_CAPACITY = 0x4000000;
        /// <summary>
        /// IP包最大长度
        /// </summary>
        public const int WINTUN_MAX_IP_PACKET_SIZE = 0xFFFF;
        /// <summary>
        /// wintundll的文件名
        /// </summary>
        public const string WintunDll = "wintun.dll";
        public const long ERROR_NO_MORE_ITEMS = 259L;
    }
    /// <summary>
    /// 全局记录器回调
    /// </summary>
    /// <param name="loggerLevel">日志等级</param>
    /// <param name="Timestamp">从1601-01-01 UTC.开始的Ticks数</param>
    /// <param name="Message">错误信息</param>
    public unsafe delegate void WintunLoggerCallBack(WintunLoggerLevel loggerLevel,long Timestamp,IntPtr Message);
    public unsafe static class WintunAPI
    {

        /// <summary>
        /// 创建新的 Wintun 适配器。
        /// </summary>
        /// <param name="Name">适配器的请求名称。最多 MAX_ADAPTER_NAME-1 个字符的零终止字符串。</param>
        /// <param name="TunnelType">适配器隧道类型的名称。最多 MAX_ADAPTER_NAME-1 个字符的零终止字符串。</param>
        /// <param name="RequestedGUID">
        /// 创建的网络适配器的 GUID，然后确定性地影响 NLA 生成。
        /// 如果设置为 NULL，则系统会随机选择 GUID，从而为每个新适配器创建一个新的 NLA 条目。
        /// 它之所以称为“请求的”GUID，是因为它使用的 API 是完全未记录的，因此其使用可能会有一些有趣的复杂情况。</param>
        /// <returns></returns>
        [DllImport(Const.WintunDll, EntryPoint = "WintunCreateAdapter",CharSet = CharSet.Unicode)]
        public static extern  IntPtr WintunCreateAdapter(string Name, string TunnelType, IntPtr RequestedGUID);

        /// <summary>
        /// 打开现有的 Wintun 适配器。
        /// </summary>
        /// <param name="Name">适配器的请求名称。最多 MAX_ADAPTER_NAME-1 个字符的零终止字符串。</param>
        /// <returns></returns>
        [DllImport(Const.WintunDll, EntryPoint = "WintunOpenAdapter", CharSet = CharSet.Unicode)]
        public static extern IntPtr WintunOpenAdapter(string Name);

        /// <summary>
        /// 释放 Wintun 适配器资源，如果适配器是使用 WintunCreateAdapter 创建的，则删除适配器。
        /// </summary>
        /// <param name="Adapter">使用 WintunCreateAdapter 或 WintunOpenAdapter 获取的适配器句柄。</param>
        [DllImport(Const.WintunDll, EntryPoint = "WintunCloseAdapter")]
        public static extern void WintunCloseAdapter(IntPtr Adapter);
        /// <summary>
        /// 如果不再使用适配器，则删除 Wintun 驱动程序。
        /// </summary>
        /// <returns>如果函数成功，则返回值为非零。如果函数失败，则返回值为零。  </returns>

        [DllImport(Const.WintunDll, EntryPoint = "WintunDeleteDriver")]
        public static extern int WintunDeleteDriver();
        /// <summary>
        /// 返回适配器的 LUID。
        /// </summary>
        /// <param name="Adapter">使用 WintunOpenAdapter 或 WintunCreateAdapter 获取的适配器句柄</param>
        /// <param name="Luid">指向 LUID 的指针以接收适配器 LUID。</param>

        [DllImport(Const.WintunDll, EntryPoint = "WintunGetAdapterLUID")]
        public static extern void WintunGetAdapterLUID(IntPtr Adapter, IntPtr Luid);

        /// <summary>
        /// 确定当前加载的 Wintun 驱动程序的版本。
        /// 若要获取扩展的错误信息，请调用 GetLastError。可能的错误包括： ERROR_FILE_NOT_FOUND Wintun 未加载
        /// </summary>
        /// <returns>如果函数成功，则返回值为版本号。如果函数失败，则返回值为零。</returns>
        [DllImport(Const.WintunDll, EntryPoint = "WintunGetRunningDriverVersion")]
        public static extern int WintunGetRunningDriverVersion();
        /// <summary>
        /// 设置记录器回调函数。
        /// </summary>
        /// <param name="wintunLoggerCallBack">
        /// 指向回调函数的指针，用作新的全局记录器。NewLogger 可以同时从各种线程调用。
        /// 如果日志记录需要序列化，则必须在 NewLogger 中处理序列化。设置为 NULL 以禁用。
        /// </param>
        /// <returns></returns>
        [DllImport(Const.WintunDll, EntryPoint = "WintunSetLogger", CharSet = CharSet.Unicode)]
        public static extern int WintunSetLogger(WintunLoggerCallBack wintunLoggerCallBack);

        /// <summary>
        /// 启动 Wintun 会话。
        /// </summary>
        /// <param name="Adapter">使用 WintunOpenAdapter 或 WintunCreateAdapter 获取的适配器句柄</param>
        /// <param name="Capacity">容量。必须介于 WINTUN_MIN_RING_CAPACITY 和 WINTUN_MAX_RING_CAPACITY 之间（包括）并且必须是 2 的幂。</param>
        /// <returns>Wintun 会话句柄。必须与 WintunEndSession 一起使用。如果函数失败，则返回值为 NULL。</returns>

        [DllImport(Const.WintunDll, EntryPoint = "WintunStartSession")]
        public static extern IntPtr WintunStartSession(IntPtr Adapter, uint Capacity);
        /// <summary>
        /// 结束 Wintun 会话。
        /// </summary>
        /// <param name="Session">使用 WintunStartSession 获取的 Wintun 会话句柄</param>
        /// <returns></returns>
        [DllImport(Const.WintunDll, EntryPoint = "WintunEndSession")]
        public static extern void WintunEndSession(IntPtr Session);
        //HANDLE WintunGetReadWaitEvent (WINTUN_SESSION_HANDLE Session)
        /// <summary>
        /// 获取 Wintun 会话的读取-等待事件句柄。
        /// </summary>
        /// <param name="Session">使用 WintunStartSession 获取的 Wintun 会话句柄</param>
        /// <returns>
        /// 用于接收事件句柄的指针，用于在读取时等待可用数据。
        /// 如果 WintunReceivePackets 返回ERROR_NO_MORE_ITEMS（在重负载下旋转一段时间后），请等待此事件发出信号，然后再重试 WintunReceivePackets。
        /// 不要在此事件上调用 CloseHandle - 它由会话管理。</returns>
        [DllImport(Const.WintunDll, EntryPoint = "WintunGetReadWaitEvent")]
        public static extern IntPtr WintunGetReadWaitEvent(IntPtr Session);

        /// <summary>
        /// 检索一个或数据包。使用数据包内容后，使用从此函数返回的数据包调用 WintunReleaseReceivePacket 以释放内部缓冲区。此函数是线程安全的。
        /// </summary>
        /// <param name="Session">使用 WintunStartSession 获取的 Wintun 会话句柄</param>
        /// <param name="PacketSize">用于接收数据包大小的指针。</param>
        /// <returns>指向第 3 层 IPv4 或 IPv6 数据包的指针。客户可以随意修改其内容。如果函数失败，则返回值为 NULL。</returns>
        [DllImport(Const.WintunDll, EntryPoint = "WintunReceivePacket")]
        public static extern IntPtr WintunReceivePacket(IntPtr Session,ref int PacketSize);
        /// <summary>
        /// 在客户端处理收到的数据包后释放内部缓冲区。此函数是线程安全的。
        /// </summary>
        /// <param name="Session">使用 WintunStartSession 获取的 Wintun 会话句柄</param>
        /// <param name="Packet">使用 WintunReceivePacket 获取的数据包</param>
        [DllImport(Const.WintunDll, EntryPoint = "WintunReleaseReceivePacket")]
        public static extern void WintunReleaseReceivePacket(IntPtr Session, IntPtr Packet);
        //BYTE* WintunAllocateSendPacket (WINTUN_SESSION_HANDLE Session, DWORD PacketSize)
        /// <summary>
        /// 为要发送的数据包分配内存。内存中填满数据包数据后，调用 WintunSendPacket 发送并释放内部缓冲区。
        /// WintunAllocateSendPacket 是线程安全的，调用的 WintunAllocateSendPacket 顺序定义数据包发送顺序。
        /// </summary>
        /// <param name="Session">使用 WintunStartSession 获取的 Wintun 会话句柄</param>
        /// <param name="PacketSize">确切的数据包大小。必须小于或等于 WINTUN_MAX_IP_PACKET_SIZE。</param>
        /// <returns>
        /// 返回指向内存的指针，用于准备第 3 层 IPv4 或 IPv6 数据包以进行发送。
        /// 如果函数失败，则返回值为 NULL。
        /// 若要获取扩展的错误信息，请调用 GetLastError。可能的错误包括：ERROR_HANDLE_EOF Wintun 适配器正在终止;ERROR_BUFFER_OVERFLOW Wintun 缓冲区已满;
        /// </returns>
        [DllImport(Const.WintunDll, EntryPoint = "WintunReleaseReceivePacket")]
        public static extern IntPtr WintunAllocateSendPacket(IntPtr Session, uint PacketSize);

        /// <summary>
        /// 发送数据包并释放内部缓冲区。WintunSendPacket 是线程安全的，但调用的 WintunAllocateSendPacket 顺序定义数据包发送顺序。
        /// 这意味着尚不能保证在 WintunSendPacket 中发送数据包。
        /// </summary>
        /// <param name="Session">使用 WintunStartSession 获取的 Wintun 会话句柄</param>
        /// <param name="Packet">使用 WintunAllocateSendPacket 获取的数据包</param>
        [DllImport(Const.WintunDll, EntryPoint = "WintunSendPacket")]
        public static extern void WintunSendPacket(IntPtr Session, IntPtr Packet);

        [DllImport("Kernel32.dll", EntryPoint = "GetLastError")]
        public static extern long GetLastError();

        public static IntPtr GetPtr<T>(this T? valueObj)where T:struct
        {
            if (valueObj==null) throw new ArgumentNullException(nameof(valueObj));
            IntPtr intPtr = Marshal.AllocHGlobal(Marshal.SizeOf(valueObj));
            Marshal.StructureToPtr(valueObj, intPtr,false);
            return intPtr;
        }
        public static IntPtr GetPtr<T>() where T : struct
        {
            IntPtr intPtr = Marshal.AllocHGlobal(Marshal.SizeOf<T>());
            return intPtr;
        }
        public static T? ToStruct<T>(this IntPtr ptr) where T : struct
        {
            if (ptr!=IntPtr.Zero)return Marshal.PtrToStructure<T>(ptr);
            return null;
        }
        public static IntPtr GetPtr(this string text,Encoding? encoding=null)
        {
            if (encoding==null) encoding=Encoding.Unicode;
            var data=encoding.GetBytes(text);
            IntPtr intPtr = Marshal.AllocHGlobal(data.Length+1);
            Marshal.Copy(data, 0, intPtr, data.Length);
            return intPtr;
        }
    }

    public enum WintunLoggerLevel:int
    {
        INFO,
        WARN,
        ERR,        
    }
    [StructLayout(LayoutKind.Explicit, Size = 8)]
    public struct NetLUIDLH
    {
        [FieldOffset(0)]
        public ulong Value;

        [FieldOffset(0)]
        public InfoStruct Info;

        [StructLayout(LayoutKind.Explicit, Size = 8)]
        public struct InfoStruct
        {
            [FieldOffset(0)] public ushort IfType;
            [FieldOffset(3)] public Int3byte NetLuidIndex;
            [FieldOffset(5)] public Int3byte Reserved;
            

        }
    }
    [StructLayout(LayoutKind.Explicit, Size = 3)]
    public unsafe struct Int3byte
    {
        [FieldOffset(0)] public byte b0;
        [FieldOffset(1)] public byte b1;
        [FieldOffset(2)] public byte b2;
        public int ToInt()
        {
            return b0 | (b1 << 8) | (b2 << 16);
        }
        public override string ToString()
        {
            return ToInt().ToString();
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct WintunAdapter
    {
        public IntPtr SwDevice;
        public IntPtr DevInfo;
        public SPDevinfoData DevInfoData;
        public IntPtr InterfaceFilename;
        public Guid CfgInstanceID;
        public IntPtr DevInstanceID;
        public uint LuidIndex;
        public uint IfType;
        public uint IfIndex;
    }
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct SPDevinfoData
    {
        public uint CbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public ulong Reserved;
    }


    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public unsafe struct TunSession
    {
        public uint Capacity;
        public _Receive Receive;
        public _Send Send;
        public TunRegisterRings Descriptor;
        public IntPtr Handle;
        public struct _Receive
        {
            public uint Tail;
            public uint TailRelease;
            public uint PacketsToRelease;
            public RtlCriticalSection Lock;
        }
        public struct _Send
        {
            public uint Head;
            public uint HeadRelease;
            public uint PacketsToRelease;
            public RtlCriticalSection Lock;
        }
    }
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public unsafe struct RtlCriticalSection
    {
        public PrtlCriticalSectionDebug DebugInfo;
        public int LockCount;
        public int RecursionCount;
        public IntPtr OwningThread;
        public IntPtr LockSemaphore;
        public ulong SpinCount;
    }
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public unsafe struct PrtlCriticalSectionDebug
    {
        public  ushort Type;
        public  ushort CreatorBackTraceIndex;
        public RtlCriticalSection* CriticalSection;
        public ListEntry ProcessLocksList;
        public uint EntryCount;
        public uint ContentionCount;
        public uint Flags;
        public ushort CreatorBackTraceIndexHigh;
        public ushort Identifier;

    }
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public unsafe struct ListEntry
    {
        public ListEntry* Flink;
        public ListEntry* Blink;
    }
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public unsafe struct TunRegisterRings
    {
        public Unnamed Send;
        public Unnamed Receive;
        public struct Unnamed
        {
            public uint RingSize;
            public TunRing* Ring;
            public IntPtr TailMoved;
        }
    }
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public unsafe struct TunRing
    {
        public uint Head;
        public uint Tail;
        public int Alertable;
        public byte* Buffer;
    }
}
