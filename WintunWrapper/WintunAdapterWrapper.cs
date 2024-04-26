using System;
using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using static Vanara.PInvoke.IpHlpApi;
using static Vanara.PInvoke.Ws2_32;

namespace WintunWrapper
{

    public class WintunAdapterWrapper:IDisposable
    {
        public delegate void WintunLoggerCallBack(WintunLoggerLevel loggerLevel, DateTime dateTime, string? Message);
        public delegate void ReceiveCallBack(Span<byte> data);
        public WintunLoggerCallBack? OnLog;
        public ReceiveCallBack? OnReceive;
        public readonly Guid? ID;
        private IntPtr _AdapterPtr=IntPtr.Zero;
        public readonly string Name;
        public readonly string TunnelType;
        public bool IsOpen { get; private set; }
        private IntPtr _SessionPrt;
        public bool IsStart { get; private set; }
        public uint SessionCapacity { get; set; } = 1024*1024*1;
        private bool disposedValue;
        private bool IsQuit=false;
        private Thread? receiveThread;
        private WintunAdapterWrapper(Guid? requestedGUID, IntPtr adapterPtr, string name,string tunnelType, bool isOpen)
        {
            if (adapterPtr==IntPtr.Zero) throw new ArgumentNullException(nameof(adapterPtr));
            ID=requestedGUID;
            _AdapterPtr=adapterPtr;
            Name=name;
            TunnelType=tunnelType;
            IsOpen=isOpen;
            WintunAPI.WintunSetLogger(DefaultWintunLoggerCallBack);

        }
        private void ReceivePacket(object? obj)
        {
            Mutex mutex = new Mutex(false);
            IntPtr quitEventPtr = WintunAPI.WintunGetReadWaitEvent(_SessionPrt);
            mutex.SafeWaitHandle = new Microsoft.Win32.SafeHandles.SafeWaitHandle(quitEventPtr, false);

            while (!IsQuit)
            {
                while (IsOpen && IsStart && _SessionPrt!=IntPtr.Zero)
                {
                    try
                    {

                        
                        IntPtr PacketDataSizePtr = WintunAPI.GetPtr<int>();

                        var dataPtr=WintunAPI.WintunReceivePacket(_SessionPrt, PacketDataSizePtr);
                        int PacketDataSize = 0;
                        if(PacketDataSizePtr!=IntPtr.Zero) PacketDataSize=PacketDataSizePtr.ToStruct<int>()??0;
                        if (PacketDataSize>0 && dataPtr!=IntPtr.Zero)
                        {
                            byte[] data=ArrayPool<byte>.Shared.Rent(PacketDataSize);
                            Marshal.Copy(dataPtr, data, 0, PacketDataSize);
                            OnReceive?.Invoke(data.AsSpan(0, PacketDataSize));
                            ArrayPool<byte>.Shared.Return(data);
                            WintunAPI.WintunReleaseReceivePacket(_SessionPrt, dataPtr);
                        }
                        else
                        {
                            var errCode = WintunAPI.GetLastError();
                            switch (errCode)
                            {
                                case Const.ERROR_NO_MORE_ITEMS:
                                    mutex.WaitOne();
                                    continue;
                                default:
                                    continue;
                            }
                        }

                        
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine(e);
                    }
                }
                Thread.Sleep(1000);
            }
            Debug.WriteLine("接收线程退出");
        }
        public void Open()
        {
            _AdapterPtr=WintunAPI.WintunOpenAdapter(Name);
            if (_AdapterPtr!=IntPtr.Zero) IsOpen=true;
            
        }
        /// <summary>
        /// 阻塞启动
        /// </summary>
        public void Start(IPAddress iPAddress,uint? sessionCapacity = null)
        {
            if (IsOpen)
            {
                StartSession(iPAddress,sessionCapacity??SessionCapacity);
            }
            ReceivePacket(null);
        }
        /// <summary>
        /// 不阻塞启动
        /// </summary>
        public  void StartAsync(IPAddress iPAddress,uint? sessionCapacity = null)
        {
            if (IsOpen)
            {
                StartSession(iPAddress,sessionCapacity??SessionCapacity);
            }
            receiveThread= new Thread(ReceivePacket);
            receiveThread.IsBackground=true;
            receiveThread.Start();
        }

        public void Close()
        {
            if (_AdapterPtr==IntPtr.Zero) throw new InvalidOperationException();
            EndSession();
            WintunAPI.WintunCloseAdapter(_AdapterPtr);
            _AdapterPtr=IntPtr.Zero;
            IsOpen =false;
        }
        private bool StartSession(IPAddress iPAddress,uint Capacity=1024*1024*1)
        {
            if (_AdapterPtr==IntPtr.Zero) throw new InvalidOperationException();
            if(!IsOpen) throw new InvalidOperationException();
            if(!(Capacity>=Const.WINTUN_MIN_RING_CAPACITY && Capacity<=Const.WINTUN_MAX_RING_CAPACITY && Capacity%2==0))
                throw new ArgumentException(nameof(Capacity));
            MIB_UNICASTIPADDRESS_ROW AddressRow;
            InitializeUnicastIpAddressEntry(out AddressRow);
            var LUIDPtr = WintunAPI.GetPtr<NET_LUID>();
            WintunAPI.WintunGetAdapterLUID(_AdapterPtr, LUIDPtr);
            AddressRow.InterfaceLuid=LUIDPtr.ToStruct<NET_LUID>()!.Value;
            AddressRow.Address.Ipv4.sin_family=ADDRESS_FAMILY.AF_INET;
            AddressRow.Address.Ipv4.sin_addr=new IN_ADDR(iPAddress.GetAddressBytes());
            AddressRow.OnLinkPrefixLength = 24;
            AddressRow.DadState=NL_DAD_STATE.IpDadStatePreferred;
            CreateUnicastIpAddressEntry(ref AddressRow);
            _SessionPrt =WintunAPI.WintunStartSession(_AdapterPtr, Capacity);
            if (_SessionPrt==IntPtr.Zero) return false;
            IsStart =true;
            return true;
        }

        private void EndSession()
        {
            if (!IsStart) throw new InvalidOperationException();
            WintunAPI.WintunEndSession(_SessionPrt);
            _SessionPrt=IntPtr.Zero;
            IsStart =false;
        }

        public NetLUIDLH? GetLUID()
        {
            if (_AdapterPtr==IntPtr.Zero) throw new InvalidOperationException();
            var Intptr=WintunAPI.GetPtr<NetLUIDLH>();
            WintunAPI.WintunGetAdapterLUID(_AdapterPtr, Intptr);
            if (Intptr!=IntPtr.Zero)
            {
                return Intptr.ToStruct<NetLUIDLH>();
            }
            else
            {
                return null;
            }
        }
        public void SendPacket(byte[] packetData)
        {
            if(packetData==null)throw new ArgumentNullException(nameof(packetData));
            if(packetData.Length==0) throw new ArgumentException(nameof(packetData));
            uint dataSize = (uint)packetData.Length;
            IntPtr dataPtr=WintunAPI.WintunAllocateSendPacket(_SessionPrt, dataSize);
            Marshal.Copy(packetData, 0, dataPtr, packetData.Length);
            WintunAPI.WintunSendPacket(_SessionPrt, dataPtr);
        }
        public int GetRunningDriverVersion()
        {
            if (_AdapterPtr==IntPtr.Zero) throw new InvalidOperationException();
            return WintunAPI.WintunGetRunningDriverVersion();
        }
        
        private void DefaultWintunLoggerCallBack(WintunLoggerLevel loggerLevel, long timestamp, IntPtr Message)
        {

            var now = new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(timestamp);
            string? msg=null;
            if (Message!=IntPtr.Zero)
            {
                msg=Marshal.PtrToStringUni(Message);
            }
            OnLog?.Invoke(loggerLevel, now.ToLocalTime(), msg);
        }

        public static WintunAdapterWrapper Create(string Name="Default", string TunnelType= "Default", Guid? RequestedGUID=null)
        {
            if (RequestedGUID==null) RequestedGUID=Guid.NewGuid();
            IntPtr GUIDPtr = RequestedGUID.GetPtr();
            var adapterPtr = WintunAPI.WintunCreateAdapter(Name, TunnelType, GUIDPtr);
            return new WintunAdapterWrapper(RequestedGUID,adapterPtr, Name, TunnelType, false);
        }
        /// <summary>
        /// 如果不再使用，调用该方法删除驱动
        /// </summary>
        /// <returns>执行结果</returns>
        public static bool DeleteDriver()
        {
            return WintunAPI.WintunDeleteDriver()!=0;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: 释放托管状态(托管对象)
                    IsQuit = true;
                }
                
                // TODO: 释放未托管的资源(未托管的对象)并重写终结器
                // TODO: 将大型字段设置为 null
                disposedValue=true;
                if (_SessionPrt!=IntPtr.Zero)
                {
                    IsStart=false;
                    WintunAPI.WintunEndSession(_SessionPrt);
                    _SessionPrt=IntPtr.Zero;
                    
                }
                if (_AdapterPtr!=IntPtr.Zero)
                {
                    IsOpen=false;
                    WintunAPI.WintunCloseAdapter(_AdapterPtr);
                    _AdapterPtr=IntPtr.Zero;
                }

            }
        }

        // TODO: 仅当“Dispose(bool disposing)”拥有用于释放未托管资源的代码时才替代终结器
        ~WintunAdapterWrapper()
        {
            // 不要更改此代码。请将清理代码放入“Dispose(bool disposing)”方法中
            Dispose(disposing: false);
        }
        public void Dispose()
        {
            // 不要更改此代码。请将清理代码放入“Dispose(bool disposing)”方法中
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
