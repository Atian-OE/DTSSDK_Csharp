using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DTSSDK
{
    //错误的定义
    public enum ErrorDef
    {
        Ok = 0,//没有错误
        TimeoutErr = 1,//超时
        UnconnectedErr = 2,//没有连接
        SerializeErr = 3,//序列化失败
        SendErr = 4,//发送失败
        UnmarshalErr = 5,//解压回传消息错误
        UnknownErr = 6,//未知错误
    }
}
