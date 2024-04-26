
using System.Runtime.InteropServices;
using WintunWrapper;


unsafe
{

    WintunAPI.WintunCreateAdapter("Demo", "Example", Guid.NewGuid().GetPtr());
    var adapterPtr = WintunAPI.WintunOpenAdapter("Demo");
    WintunAPI.WintunSetLogger(WintunLoggerCallBack);



    Console.WriteLine(WintunAPI.WintunGetRunningDriverVersion());
    NetLUIDLH netLUIDLH = new NetLUIDLH();
    WintunAPI.WintunGetAdapterLUID(adapterPtr, &netLUIDLH);


    WintunAPI.WintunCloseAdapter(adapterPtr);
    var state=WintunAPI.WintunDeleteDriver();

    Console.WriteLine();

}

unsafe void WintunLoggerCallBack(WintunLoggerLevel loggerLevel, long Timestamp, char* Message)
{
    //timestamp in in 100ns intervals since 1601-01-01 UTC. 
    var now=new DateTime(1601,1,1,0,0,0,DateTimeKind.Local).AddTicks(Timestamp);
    Console.WriteLine($"{loggerLevel} {now.ToLocalTime()} {new string(Message)}");
}