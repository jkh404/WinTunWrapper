
using System.Runtime.InteropServices;
using WintunWrapper;

using WintunAdapterWrapper wintun = WintunAdapterWrapper.Create();
wintun.OnLog=Wintun_OnLog;
wintun.OnReceive=(data)=> 
{
    Console.WriteLine($"收到数据包:{data.Length} Byte");
};
wintun.Open();
wintun.Start();
wintun.Dispose();
WintunAdapterWrapper.DeleteDriver();

void Wintun_OnLog(WintunLoggerLevel loggerLevel, DateTime dateTime, string? Message)
{
    Console.WriteLine($"{Message}");
}
