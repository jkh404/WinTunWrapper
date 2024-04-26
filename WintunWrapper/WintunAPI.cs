using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace  WintunWrapper
{
    /*
WintunAllocateSendPacket	1	0x00006750	WintunAllocateSendPacket	
WintunCloseAdapter	2	0x00002690	WintunCloseAdapter	
WintunCreateAdapter	3	0x00003070	WintunCreateAdapter	
WintunDeleteDriver	4	0x00005020	WintunDeleteDriver	
WintunEndSession	5	0x00006550	WintunEndSession	
WintunGetAdapterLUID	6	0x00002BF0	WintunGetAdapterLUID	
WintunGetReadWaitEvent	7	0x000065E0	WintunGetReadWaitEvent	
WintunGetRunningDriverVersion	8	0x00004550	WintunGetRunningDriverVersion	
WintunOpenAdapter	9	0x00003A80	WintunOpenAdapter	
WintunReceivePacket	10	0x000065F0	WintunReceivePacket	
WintunReleaseReceivePacket	11	0x000066C0	WintunReleaseReceivePacket	
WintunSendPacket	12	0x00006810	WintunSendPacket	
WintunSetLogger	13	0x00005350	WintunSetLogger	
WintunStartSession	14	0x000062B0	WintunStartSession	

     
     */
    public static class Const
    {
        public const int MAX_ADAPTER_NAME = 128;
        /// <summary>
        /// 128kiB
        /// </summary>
        public const uint WINTUN_MIN_RING_CAPACITY = 0x20000;
        /// <summary>
        /// 64MiB
        /// </summary>
        public const uint WINTUN_MAX_RING_CAPACITY = 0x4000000;
        
    }
    /// <summary>
    /// 全局记录器回调
    /// </summary>
    /// <param name="loggerLevel">日志等级</param>
    /// <param name="Timestamp">从1601-01-01 UTC.开始的Ticks数</param>
    /// <param name="Message">错误信息</param>
    public unsafe delegate void WintunLoggerCallBack(WintunLoggerLevel loggerLevel,long Timestamp,char* Message);
    public unsafe static class WintunAPI
    {
        const string WintunDll = "wintun.dll";


        [DllImport(WintunDll, EntryPoint = "WintunAllocateSendPacket")]
        public static extern IntPtr WintunAllocateSendPacket(TunSession session, uint PacketSize);

        /// <summary>
        /// Creates a new Wintun adapter.
        /// </summary>
        /// <param name="Name">The requested name of the adapter. Zero-terminated string of up to MAX_ADAPTER_NAME-1 characters.</param>
        /// <param name="TunnelType"> Name of the adapter tunnel type. Zero-terminated string of up to MAX_ADAPTER_NAME-1 characters.</param>
        /// <param name="RequestedGUID">The GUID of the created network adapter, which then influences NLA generation deterministically. If it is set to NULL, the GUID is chosen by the system at random, and hence a new NLA entry is created for each new adapter. It is called "requested" GUID because the API it uses is completely undocumented, and so there could be minor interesting complications with its usage.</param>
        /// <returns></returns>
        [DllImport(WintunDll, EntryPoint = "WintunCreateAdapter",CharSet = CharSet.Unicode)]
        public static extern  WintunAdapter* WintunCreateAdapter(string Name, string TunnelType, IntPtr RequestedGUID);

        /// <summary>
        /// Opens an existing Wintun adapter.
        /// </summary>
        /// <param name="Name">The requested name of the adapter. Zero-terminated string of up to MAX_ADAPTER_NAME-1 characters.</param>
        /// <returns></returns>
        [DllImport(WintunDll, EntryPoint = "WintunOpenAdapter", CharSet = CharSet.Unicode)]
        public static extern WintunAdapter* WintunOpenAdapter(string Name);

        //void WintunCloseAdapter (WINTUN_ADAPTER_HANDLE Adapter)
        [DllImport(WintunDll, EntryPoint = "WintunCloseAdapter")]
        public static extern void WintunCloseAdapter(WintunAdapter* Adapter);

        [DllImport(WintunDll, EntryPoint = "WintunDeleteDriver")]
        public static extern int WintunDeleteDriver();

        [DllImport(WintunDll, EntryPoint = "WintunGetAdapterLUID")]
        public static extern void WintunGetAdapterLUID(WintunAdapter* Adapter, NetLUIDLH* Luid);


        [DllImport(WintunDll, EntryPoint = "WintunGetRunningDriverVersion")]
        public static extern int WintunGetRunningDriverVersion();
        /// <summary>
        /// 设置记录器回调函数。
        /// </summary>
        /// <param name="wintunLoggerCallBack">指向回调函数的指针，用作新的全局记录器。NewLogger 可以同时从各种线程调用。如果日志记录需要序列化，则必须在 NewLogger 中处理序列化。设置为 NULL 以禁用。</param>
        /// <returns></returns>
        [DllImport(WintunDll, EntryPoint = "WintunSetLogger", CharSet = CharSet.Unicode)]
        public static extern int WintunSetLogger(WintunLoggerCallBack wintunLoggerCallBack);

        /// <summary>
        /// 启动 Wintun 会话。
        /// </summary>
        /// <param name="Adapter">使用 WintunOpenAdapter 或 WintunCreateAdapter 获取的适配器句柄</param>
        /// <param name="Capacity">容量。必须介于 WINTUN_MIN_RING_CAPACITY 和 WINTUN_MAX_RING_CAPACITY 之间（包括）并且必须是 2 的幂。</param>
        /// <returns>Wintun 会话句柄。必须与 WintunEndSession 一起使用。如果函数失败，则返回值为 NULL。</returns>

        [DllImport(WintunDll, EntryPoint = "WintunStartSession")]
        public static extern TunSession* WintunStartSession(WintunAdapter* Adapter, uint Capacity);


        public static IntPtr GetPtr<T>(this T valueObj)where T:struct
        {
            IntPtr intPtr = Marshal.AllocHGlobal(Marshal.SizeOf(valueObj));
            Marshal.StructureToPtr(valueObj, intPtr,false);
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
        /// <summary>
        /// Informational
        /// </summary>
        INFO,
        /// <summary>
        /// Warning
        /// </summary>
        WARN,
        /// <summary>
        /// Error
        /// </summary>
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
