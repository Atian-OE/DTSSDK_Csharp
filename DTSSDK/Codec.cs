using DTSSDK.Model;
using Google.Protobuf;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DTSSDK
{
    public class Codec
    {
        public static byte[] Encode<T>(T obj) where T : IMessage<T>
        {
            Type type = obj.GetType();

            byte[] data = obj.ToByteArray();
            byte[] cache = new byte[data.Length + 5];
            byte[] leng= BitConverter.GetBytes(data.Length);
            if (BitConverter.IsLittleEndian) {
                Array.Reverse(leng);
            }
            Array.Copy(leng, cache, leng.Length);

            
            if (type == typeof(GetDefenceZoneRequest))
            {
                cache[4] = (byte)MsgID.GetDefenceZoneRequestId;
            }
            else if (type == typeof(SetDeviceRequest))
            {
                cache[4] = (byte)MsgID.SetDeviceRequestId;
            }
            else if (type == typeof(GetDeviceIDRequest))
            {
                cache[4] = (byte)MsgID.GetDeviceIdrequestId;
            }
            else if (type == typeof(HeartBeat)) {
                cache[4] = (byte)MsgID.HeartBeatId;
            }
            Array.Copy(data,0, cache,5, data.Length);
            return cache;
        }
    }
}
