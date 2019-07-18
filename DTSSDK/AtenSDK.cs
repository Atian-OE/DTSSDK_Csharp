using DTSSDK.Model;
using Google.Protobuf;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace DTSSDK
{
    public class DTSClient
    {
        private struct WaitPack
        {
            public Action<MsgID, byte[]> action;
            public MsgID msg_id;
        }

        /// <summary>
        /// 连接的回调
        /// </summary>
        private Action<String> connected_action;
        /// <summary>
        /// 断开的回调
        /// </summary>
        private Action<String> disconnected_action;
        /// <summary>
        /// 异常回调
        /// </summary>
        private Action<Exception> error_action;
        /// <summary>
        /// 创建防区通知
        /// </summary>
        private Action<ZoneTempNotify> zone_temp_notify;
        /// <summary>
        /// 更新防区通知
        /// </summary>
        private Action<ZoneAlarmNotify> zone_alarm_notify;
        /// <summary>
        /// 更新防区状态通知
        /// </summary>
        private Action<DeviceEventNotify> device_event_notify;
        /// <summary>
        /// 更新防区布防状态通知
        /// </summary>
        private Action<TempSignalNotify> temp_signal_notify;


        private string host = "127.0.0.1";
        private bool is_connected = false;
        private TcpClient tcp_client;
        //自动连接线程
        private Thread auto_connect_th = null;
        private bool break_auto_connect_th = false;
        //心跳线程
        private Thread heart_beat_th = null;
        private bool break_heart_beat_th = false;
        //接受线程
        private Thread receive_th = null;
        private bool break_receive_th = false;

        //接受数据的缓存
        private ByteBuffer recv_buf = ByteBuffer.Allocate(1024);
        //等待这个包的回传
        private static LinkedList<WaitPack> wait_pack = new LinkedList<WaitPack>();
        private static object wait_pack_look = new object();

        /// <summary>
        /// 初始化连接
        /// </summary>
        /// <param name="_host">远端设备ip地址</param>
        public DTSClient(string _host)
        {
            host = _host;
        }

        /// <summary>
        /// 开始
        /// </summary>
        public void Start()
        {
            Close();

            auto_connect_th = new Thread(auto_connect);
            auto_connect_th.Start();

            heart_beat_th = new Thread(heart_beat);
            heart_beat_th.Start();

            receive_th = new Thread(receive);
            receive_th.Start();
        }

        /// <summary>
        /// 关闭资源
        /// </summary>
        public void Close()
        {
            lock (wait_pack_look)
            {
                wait_pack.Clear();
            }

            if (auto_connect_th != null)
            {
                break_auto_connect_th = true;
                auto_connect_th.Join();
            }

            if (heart_beat_th != null)
            {
                break_heart_beat_th = true;
                heart_beat_th.Join();
            }

            if (is_connected)
            {
                disconnect();
            }

            if (receive_th != null)
            {
                break_receive_th = true;
                receive_th.Join();
            }
        }

        //断开连接
        private void disconnect()
        {
            if (is_connected)
            {
                tcp_client.Close();
                is_connected = false;
                receive_handle(MsgID.DisconnectId, null);
            }
        }

        //写入数据到socket
        private ErrorDef write(byte[] data)
        {
            if (is_connected)
            {
                try
                {
                    tcp_client.GetStream().Write(data, 0, data.Length);
                }
                catch (Exception ex)
                {
                    if (error_action != null)
                    {
                        error_action.Invoke(ex);
                    }
                    is_connected = false;
                    return ErrorDef.SendErr;
                }
                return ErrorDef.Ok;
            }
            else
            {
                return ErrorDef.UnconnectedErr;
            }
        }

        //自动连接
        private void auto_connect(object o)
        {
            break_auto_connect_th = false;
            int reconnect_time_full = 10;
            int reconnect_time = 10;
            while (!break_auto_connect_th)
            {

                if (reconnect_time == reconnect_time_full)
                {
                    if (!is_connected)
                    {
                        try
                        {
                            tcp_client = new TcpClient();
                            tcp_client.SendTimeout = 2000;
                            tcp_client.ReceiveTimeout = 0;
                            tcp_client.Connect(host, 17083);
                            is_connected = tcp_client.Connected;

                            receive_handle(MsgID.ConnectId, null);

                        }
                        catch (Exception ex)
                        {
                            if (error_action != null)
                            {
                                error_action.Invoke(ex);
                            }
                            is_connected = false;
                        }
                    }

                    reconnect_time = 0;
                }
                reconnect_time++;
                Thread.Sleep(1000);
            }
            auto_connect_th = null;
        }

        //心跳
        private void heart_beat(object o)
        {
            break_heart_beat_th = false;
            int heart_beat_time_full = 5;
            int heart_beat_time = 5;
            while (!break_heart_beat_th)
            {
                if (heart_beat_time == heart_beat_time_full)
                {
                    if (is_connected)
                    {
                        HeartBeat model = new HeartBeat();
                        byte[] bytes = Codec.Encode(model);
                        write(bytes);
                    }

                    heart_beat_time = 0;
                }
                heart_beat_time++;
                Thread.Sleep(1000);
            }
            heart_beat_th = null;
        }

        //接受
        private void receive(object o)
        {
            break_receive_th = false;
            recv_buf.Clear();
            byte[] buf = new byte[1024];
            while (!break_receive_th)
            {
                if (!is_connected)
                {
                    Thread.Sleep(1000);
                    recv_buf.Clear();
                    continue;
                }
                try
                {
                    int n = tcp_client.GetStream().Read(buf, 0, buf.Length);
                    recv_buf.WriteBytes(buf, n);
handle_buf:
                    if (recv_buf.ReadableBytes() < 5)
                    {
                        continue;
                    }
                    recv_buf.MarkReaderIndex();
                    int pkg_size = recv_buf.ReadInt();
                    byte cmd = recv_buf.ReadByte();
                    //不够
                    if (recv_buf.ReadableBytes() < pkg_size)
                    {
                        recv_buf.ResetReaderIndex();
                        continue;
                    }
                    byte[] bytes = new byte[pkg_size];
                    recv_buf.ReadBytes(bytes, 0, pkg_size);
                    receive_handle((MsgID)cmd, bytes);

                    recv_buf.DiscardReadBytes();
                    goto handle_buf;
                }
                catch (Exception ex)
                {
                    is_connected = false;
                    if (error_action != null)
                    {
                        error_action.Invoke(ex);
                    }
                }
            }
            receive_th = null;
        }


        //接受处理
        private void receive_handle(MsgID cmd_id, byte[] data)
        {
            lock (wait_pack_look)
            {
                foreach (var item in wait_pack)
                {
                    if (item.msg_id == cmd_id)
                    {
                        item.action.Invoke(cmd_id, data);
                        return;
                    }
                }
            }

            switch (cmd_id)
            {
                case MsgID.ConnectId:
                    {
                        SetDeviceReply reply;
                        SetDeviceRequest(out reply);
                        if (connected_action != null)
                        {
                            connected_action.Invoke(host);
                        }
                    }
                    break;
                case MsgID.DisconnectId:
                    {
                        lock (wait_pack_look)
                        {
                            wait_pack.Clear();
                        }
                        recv_buf.Clear();
                        if (disconnected_action != null)
                        {
                            disconnected_action.Invoke(host);
                        }

                    }
                    break;
                case MsgID.ZoneTempNotifyId:
                    {

                        ZoneTempNotify notify = ZoneTempNotify.Parser.ParseFrom(data);
                        if (notify == null)
                        {
                            return;
                        }
                        if (zone_temp_notify != null)
                        {
                            zone_temp_notify.Invoke(notify);
                        }
                    }
                    break;
                case MsgID.ZoneAlarmNotifyId:
                    {
                        ZoneAlarmNotify notify = ZoneAlarmNotify.Parser.ParseFrom(data);
                        if (notify == null)
                        {
                            return;
                        }
                        if (zone_alarm_notify != null)
                        {
                            zone_alarm_notify.Invoke(notify);
                        }
                    }
                    break;
                case MsgID.DeviceEventNotifyId:
                    {
                        DeviceEventNotify notify = DeviceEventNotify.Parser.ParseFrom(data);
                        if (notify == null)
                        {
                            return;
                        }
                        if (device_event_notify != null)
                        {
                            device_event_notify.Invoke(notify);
                        }
                    }
                    break;
                case MsgID.TempSignalNotifyId:
                    {
                        TempSignalNotify notify = TempSignalNotify.Parser.ParseFrom(data);
                        if (notify == null)
                        {
                            return;
                        }
                        if (temp_signal_notify != null)
                        {
                            temp_signal_notify.Invoke(notify);
                        }
                    }
                    break;

            }
        }



        /// <summary>
        /// 查询并获得回执
        /// </summary>
        /// <typeparam name="T1">请求类</typeparam>
        /// <typeparam name="T2">返回类</typeparam>
        /// <param name="req">请求参数</param>
        /// <param name="wait_id">等待这个包回传</param>
        /// <param name="reply">回传</param>
        /// <returns></returns>
        private ErrorDef req_wait_reply<T1, T2>(T1 req, MsgID wait_id, out T2 reply) where T1 : IMessage<T1> where T2 : IMessage<T2>, new()
        {
            reply = default(T2);
            ErrorDef result_err = ErrorDef.Ok;
            byte[] req_bytes = Codec.Encode(req);
            result_err = write(req_bytes);
            if (result_err != ErrorDef.Ok)
            {
                return result_err;
            }

            //超过这个时间
            int timeout = 10000;
            result_err = ErrorDef.TimeoutErr;
            T2 _reply = default(T2);
            Action<MsgID, byte[]> action = (msg_id, data) =>
            {
                MessageParser<T2> parser = new MessageParser<T2>(() => new T2());
                _reply = parser.ParseFrom(data);
                timeout = 0;
                result_err = ErrorDef.Ok;
            };

            WaitPack pack = new WaitPack();
            pack.action = action;
            pack.msg_id = wait_id;

            lock (wait_pack_look)
            {
                wait_pack.AddLast(pack);
            }

            Thread wait_th = new Thread((o) =>
            {
                while (timeout > 0)
                {
                    Thread.Sleep(50);
                    timeout -= 50;
                }
            });
            wait_th.Start();
            wait_th.Join();

            lock (wait_pack_look)
            {
                wait_pack.Remove(pack);
            }
            reply = _reply;

            return result_err;
        }

        /// <summary>
        /// 设置远端设备ip地址
        /// </summary>
        /// <param name="_host"></param>
        public void SetHost(string _host)
        {
            host = _host;
            if (is_connected)
            {
                disconnect();
            }

        }

        /// <summary>
        /// 获得所有防区
        /// </summary>
        /// <param name="ch_id">通道</param>
        /// <param name="search">分区名搜索</param>
        /// <param name="reply">返回查询结果</param>
        public ErrorDef GetDefenceZone(int ch_id, string search, out GetDefenceZoneReply reply)
        {
            GetDefenceZoneRequest req = new GetDefenceZoneRequest();

            req.Channel = ch_id;
            req.Search = search;
            ErrorDef result_err = req_wait_reply(req, MsgID.GetDefenceZoneReplyId, out reply);
            return result_err;
        }


        /// <summary>
        /// 获得设备id
        /// </summary>
        /// <param name="reply">返回查询结果</param>
        public ErrorDef GetDeviceID(out GetDeviceIDReply reply)
        {
            GetDeviceIDRequest req = new GetDeviceIDRequest();

            ErrorDef result_err = req_wait_reply(req, MsgID.GetDeviceIdreplyId, out reply);
            return result_err;
        }


        /// <summary>
        /// 设置设备消息回调
        /// </summary>
        /// <param name="reply">返回查询结果</param>
        private ErrorDef SetDeviceRequest(out SetDeviceReply reply)
        {
            SetDeviceRequest req = new SetDeviceRequest();
            req.ZoneTempNotifyEnable = zone_temp_notify != null;
            req.ZoneAlarmNotifyEnable = zone_alarm_notify != null;
            req.FiberStatusNotifyEnable = device_event_notify != null;
            req.TempSignalNotifyEnable = temp_signal_notify != null;

            ErrorDef result_err = req_wait_reply(req, MsgID.SetDeviceReplyId, out reply);
            return result_err;
        }

        /// <summary>
        /// 回调连接到服务器
        /// </summary>
        /// <param name="action">回调委托</param>
        public void CallConnected(Action<String> action)
        {
            connected_action = action;
        }

        /// <summary>
        /// 禁用回调连接到服务器
        /// </summary>
        /// <param name="action">回调委托</param>
        public void DisableConnected()
        {
            connected_action = null;
        }

        /// <summary>
        /// 回调断开连接到服务器
        /// </summary>
        /// <param name="action">回调委托</param>
        public void CallDisconnected(Action<String> action)
        {
            disconnected_action = action;
        }

        /// <summary>
        /// 禁用回调断开连接到服务器
        /// </summary>
        public void DisableDisconnected()
        {
            disconnected_action = null;
        }

        /// <summary>
        /// 回调错误
        /// </summary>
        /// <param name="action">回调委托</param>
        public void CallErrorAction(Action<Exception> action)
        {
            error_action = action;
        }

        /// <summary>
        /// 禁用回调错误
        /// </summary>
        public void DisableErrorAction()
        {
            error_action = null;
        }

        /// <summary>
        /// 回调分区温度更新的通知
        /// </summary>
        /// <param name="action">回调委托</param>
        /// <returns>返回错误代码</returns>
        public ErrorDef CallZoneTempNotify(Action<ZoneTempNotify> action)
        {
            zone_temp_notify = action;
            SetDeviceReply reply;
            ErrorDef err = SetDeviceRequest(out reply);
            if (err != ErrorDef.Ok)
            {
                return err;
            }
            if (!reply.Success)
            {
                return ErrorDef.UnknownErr;
            }
            return ErrorDef.Ok;
        }

        /// <summary>
        /// 禁用回调分区温度更新的通知
        /// </summary>
        /// <returns>返回错误代码</returns>
        public ErrorDef DisableZoneTempNotify()
        {
            zone_temp_notify = null;
            SetDeviceReply reply;
            ErrorDef err = SetDeviceRequest(out reply);
            if (err != ErrorDef.Ok)
            {
                return err;
            }
            if (!reply.Success)
            {
                return ErrorDef.UnknownErr;
            }
            return ErrorDef.Ok;
        }

        /// <summary>
        /// 回调分区警报更新的通知
        /// </summary>
        /// <param name="action">回调委托</param>
        /// <returns>返回错误代码</returns>
        public ErrorDef CallZoneAlarmNotify(Action<ZoneAlarmNotify> action)
        {
            zone_alarm_notify = action;
            SetDeviceReply reply;
            ErrorDef err = SetDeviceRequest(out reply);
            if (err != ErrorDef.Ok)
            {
                return err;
            }
            if (!reply.Success)
            {
                return ErrorDef.UnknownErr;
            }
            return ErrorDef.Ok;
        }

        /// <summary>
        /// 禁用回调分区警报更新的通知
        /// </summary>
        /// <returns>返回错误代码</returns>
        public ErrorDef DisableZoneAlarmNotify()
        {
            zone_alarm_notify = null;
            SetDeviceReply reply;
            ErrorDef err = SetDeviceRequest(out reply);
            if (err != ErrorDef.Ok)
            {
                return err;
            }
            if (!reply.Success)
            {
                return ErrorDef.UnknownErr;
            }
            return ErrorDef.Ok;
        }

        /// <summary>
        /// 回调光纤状态更新的通知
        /// </summary>
        /// <param name="action">回调委托</param>
        /// <returns>返回错误代码</returns>
        public ErrorDef CallDeviceEventNotify(Action<DeviceEventNotify> action)
        {
            device_event_notify = action;
            SetDeviceReply reply;
            ErrorDef err = SetDeviceRequest(out reply);
            if (err != ErrorDef.Ok)
            {
                return err;
            }
            if (!reply.Success)
            {
                return ErrorDef.UnknownErr;
            }
            return ErrorDef.Ok;
        }

        /// <summary>
        /// 禁用回调分区警报更新的通知
        /// </summary>
        /// <returns>返回错误代码</returns>
        public ErrorDef DisableDeviceEventNotify()
        {
            device_event_notify = null;
            SetDeviceReply reply;
            ErrorDef err = SetDeviceRequest(out reply);
            if (err != ErrorDef.Ok)
            {
                return err;
            }
            if (!reply.Success)
            {
                return ErrorDef.UnknownErr;
            }
            return ErrorDef.Ok;
        }

        /// <summary>
        /// 回调温度信号更新的通知
        /// </summary>
        /// <param name="action">回调委托</param>
        /// <returns>返回错误代码</returns>
        public ErrorDef CallTempSignalNotify(Action<TempSignalNotify> action)
        {
            temp_signal_notify = action;
            SetDeviceReply reply;
            ErrorDef err = SetDeviceRequest(out reply);
            if (err != ErrorDef.Ok)
            {
                return err;
            }
            if (!reply.Success)
            {
                return ErrorDef.UnknownErr;
            }
            return ErrorDef.Ok;
        }

        /// <summary>
        /// 禁用回调温度信号更新的通知
        /// </summary>
        /// <returns>返回错误代码</returns>
        public ErrorDef DisableTempSignalNotify()
        {
            temp_signal_notify = null;
            SetDeviceReply reply;
            ErrorDef err = SetDeviceRequest(out reply);
            if (err != ErrorDef.Ok)
            {
                return err;
            }
            if (!reply.Success)
            {
                return ErrorDef.UnknownErr;
            }
            return ErrorDef.Ok;
        }
    }
}
