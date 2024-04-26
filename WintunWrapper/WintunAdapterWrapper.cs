using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace WintunWrapper
{
    public class WintunAdapterWrapper:IDisposable
    {
        public delegate void WintunLoggerCallBack(WintunLoggerLevel loggerLevel, DateTime dateTime, string? Message);
        public event WintunLoggerCallBack? OnLog;
        private IntPtr _AdapterPtr=IntPtr.Zero;
        public readonly string Name;
        public readonly string TunnelType;

        public bool IsOpen { get; private set; }
        private bool disposedValue;

        private WintunAdapterWrapper(IntPtr adapterPtr, string name,string tunnelType, bool isOpen)
        {
            if (adapterPtr==IntPtr.Zero) throw new ArgumentNullException(nameof(adapterPtr));
            _AdapterPtr=adapterPtr;
            Name=name;
            TunnelType=tunnelType;
            IsOpen=isOpen;
            if(IsOpen) WintunAPI.WintunSetLogger(DefaultWintunLoggerCallBack);
        }
        public void Open()
        {
            _AdapterPtr=WintunAPI.WintunOpenAdapter(Name);
            if (_AdapterPtr!=IntPtr.Zero) IsOpen=true;
            if (IsOpen) WintunAPI.WintunSetLogger(DefaultWintunLoggerCallBack);
        }
        public void Close()
        {
            if (_AdapterPtr==IntPtr.Zero) throw new InvalidOperationException();
            WintunAPI.WintunCloseAdapter(_AdapterPtr);
            IsOpen=false;
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
                //// 假设你的 IntPtr 对象叫做 quitEventPtr
                //IntPtr quitEventPtr = IntPtr.Zero; // 你的 IntPtr 对象

                //// 创建一个 AutoResetEvent 对象，并将其 SafeWaitHandle 属性设置为你的 IntPtr 对象
                //AutoResetEvent quitEvent = new AutoResetEvent(false);
                //quitEvent.SafeWaitHandle = new Microsoft.Win32.SafeHandles.SafeWaitHandle(quitEventPtr, false);
                //// 等待 quitEvent
                //bool signaled = quitEvent.WaitOne(1000); // 等待 1 秒
                return null;
            }
        }
        public int GetRunningDriverVersion()
        {
            if (_AdapterPtr==IntPtr.Zero) throw new InvalidOperationException();
            return WintunAPI.WintunGetRunningDriverVersion();
        }
        
        private void DefaultWintunLoggerCallBack(WintunLoggerLevel loggerLevel, long timestamp, IntPtr Message)
        {

            var now = new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Local).AddTicks(timestamp);
            string? msg=null;
            if (Message!=IntPtr.Zero)
            {
                msg=Marshal.PtrToStringUni(Message);
            }
            OnLog?.Invoke(loggerLevel, now, msg);
        }

        public static WintunAdapterWrapper Create(string Name="Default", string TunnelType= "Default", Guid? RequestedGUID=null)
        {
            if (RequestedGUID==null) RequestedGUID=Guid.NewGuid();
            IntPtr GUIDPtr = RequestedGUID.GetPtr();
            var adapterPtr = WintunAPI.WintunCreateAdapter(Name, TunnelType, GUIDPtr);
            return new WintunAdapterWrapper(adapterPtr, Name, TunnelType, false);
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
                }
                
                // TODO: 释放未托管的资源(未托管的对象)并重写终结器
                // TODO: 将大型字段设置为 null
                disposedValue=true;
                if (_AdapterPtr!=IntPtr.Zero)
                {
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
