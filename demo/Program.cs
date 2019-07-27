using DTSSDK;
using DTSSDK.Model;
using System;
using Google.Protobuf.Collections;

namespace demo
{
    class Program
    {
        static void Connected(string addr) {
            Console.WriteLine("连接成功",addr);
        }
        static void Disconnected(string addr)
        {
            Console.WriteLine("断开连接");
        }
        static void ErrorCallBack(Exception ex)
        {
            Console.WriteLine(ex);
        }
        static void DeviceEventNotify(DeviceEventNotify notify)
        {
            Console.WriteLine("DeviceEventNotify:" + notify.DeviceID);
        }
        static void TempSignalNotify(TempSignalNotify notify)
        {
            Console.WriteLine("TempSignalNotify:" + notify.DeviceID+notify.RealLength);
        }
        
        static void ZoneTempNotify(ZoneTempNotify notify)
        {
            
            Console.WriteLine("ZoneTempNotify:" + notify.DeviceID);
        }
       

        static void Main(string[] args)
        {
            DTSSDK.DTSClient client = new DTSSDK.DTSClient("192.168.0.50");
            client.CallConnected(Connected);
            //client.DisableConnected();
            client.CallDisconnected(Disconnected);
            //client.DisableDisconnected();
            client.CallErrorAction(ErrorCallBack);
            client.CallDeviceEventNotify(DeviceEventNotify);
            client.CallTempSignalNotify(TempSignalNotify);
            client.CallTempSignalNotify(TempSignalNotify);
            client.CallZoneTempNotify(ZoneTempNotify);

            client.Start();

            System.Threading.Thread.Sleep(2000);
            GetDefenceZoneReply reply;
            ErrorDef error= client.GetDefenceZone(1, "", out reply);
            Console.WriteLine("GetDefenceZone" + error);

            GetDeviceIDReply reply2;
            error = client.GetDeviceID(out reply2);
            Console.WriteLine("GetDeviceIDReply" + error);
            
           
            Console.ReadKey();
            client.Close();
        }
    }
}
