
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using WintunWrapper;
using System.Linq;
using System.Collections;

using WintunAdapterWrapper wintun = WintunAdapterWrapper.Create();
wintun.OnLog=Wintun_OnLog;
wintun.OnReceive=(Span<byte> data)=> 
{
    //Console.WriteLine($"收到数据包:{data.Length} Byte");
    PrintPacket(data);
};
wintun.Open();
wintun.Start(new IPAddress([166, 166, 166, 1]));
wintun.Dispose();
WintunAdapterWrapper.DeleteDriver();

void Wintun_OnLog(WintunLoggerLevel loggerLevel, DateTime dateTime, string? Message)
{
    Console.WriteLine($"{Message}");
}
void PrintPacket(Span<byte> Packet)
{
    int PacketSize = Packet.Length;
    if (Packet.Length<20)
    {
        Console.WriteLine("Received packet without room for an IP header");

    }
    int IpVersion = Packet[0] >> 4;
    int Proto;
    IPAddress FromIp;
    IPAddress ToIp;
    if (IpVersion == 4)
    {
        Proto = Packet[9];

        FromIp=new IPAddress(Packet[12..16]);
        ToIp=new IPAddress(Packet[16..20]);
        PacketSize-=20;
    }
    else if (IpVersion == 6 && Packet.Length >= 40)
    {
        Proto = Packet[35];
        FromIp=new IPAddress(Packet[8..24]);
        ToIp=new IPAddress(Packet[24..40]);
        PacketSize-=40;
    }
    else
    {
        Console.WriteLine("Unknown IP version");
        return;
    }
    if (Proto == 1 && PacketSize >= 8 && Packet[0] == 0)
    {

        Console.WriteLine($"Received IPv{IpVersion} ICMP echo reply from {FromIp} to {ToIp}");
    }
    else if(Proto == 6)
    {

        Console.WriteLine($"Received IPv{IpVersion} TCP  ({Packet.Length})Byte from {FromIp} to {ToIp}");
    }
    else
    {
        Console.WriteLine($"Received IPv{IpVersion} proto 0x{Proto:x}  packet from {FromIp} to {ToIp}");
    }

}

//void SetIP(Guid? ID, string ipAddress, string subnetMask)
//{
//    ManagementClass objMC = new ManagementClass("Win32_NetworkAdapterConfiguration");
//    ManagementObjectCollection objMOC = objMC.GetInstances();

//    foreach (ManagementObject objMO in objMOC)
//    {

//        var SettingID = objMO["SettingID"].ToString();
//        if (Guid.Parse(SettingID)==ID)
//        {
//            try
//            {
//                ManagementBaseObject setIP;
//                ManagementBaseObject newIP =
//                    objMO.GetMethodParameters("EnableStatic");
//                var list=GetList(objMO.Properties).ToList();
//                newIP["IPAddress"] = new string[] { ipAddress };
//                newIP["SubnetMask"] = new string[] { subnetMask };

//                setIP = objMO.InvokeMethod("EnableStatic", newIP, null);
//            }
//            catch (Exception)
//            {
//                throw;
//            }
//        }
//    }
//}
//IEnumerable<string> GetList(IEnumerable values)
//{
//    foreach (System.Management.PropertyData item in values)
//    {
//        yield return $"{item.Name}";
//    }
//}