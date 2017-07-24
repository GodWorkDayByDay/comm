using BatteryTestDevice.Command.BMS;
using BatteryTestDevice.Function;
using BatteryTestDevice.Global;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace BatteryTestDevice.ExternalDevice
{
    public class Network
    {
        protected Socket m_serverSocket;

        protected Socket m_clientSocket;

        public bool WriteCommunicationLog 
        {
            get
            {
                return AppSetting.GetCommunicationLogSetting();
            }
        }

        private Stopwatch m_time;

        private static readonly int m_port = 7001;

        private static object obj = new object();

        public Network()
        {
            m_clientSocket = null;
            this.m_time = new Stopwatch();
        }

        public bool IsConnected
        {
            get
            {
                if (this.m_clientSocket != null && this.m_clientSocket.Connected)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        public virtual void Init()
        {
            try
            {
                uint dummy = 0;
                byte[] inOptionValues = new byte[System.Runtime.InteropServices.Marshal.SizeOf(dummy) * 3];
                BitConverter.GetBytes((uint)1).CopyTo(inOptionValues, 0);
                BitConverter.GetBytes((uint)3000).CopyTo(inOptionValues, System.Runtime.InteropServices.Marshal.SizeOf(dummy));
                BitConverter.GetBytes((uint)3000).CopyTo(inOptionValues, System.Runtime.InteropServices.Marshal.SizeOf(dummy) * 2);

                this.m_serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                this.m_serverSocket.IOControl(IOControlCode.KeepAliveValues, inOptionValues, null);
                EndPoint endPoint = new IPEndPoint(IPAddress.Any, m_port);
                this.m_serverSocket.Bind(endPoint);
                this.m_serverSocket.Listen(500);
            }
            catch (Exception ex)
            {
                Hynn.Common.Logger.Log.Logging(System.Reflection.MethodBase.GetCurrentMethod())
                    .Info("当前域发生没有处理的异常", ex);
            }
            finally
            {
                m_serverSocket.BeginAccept(AcceptAsyncCallback, m_serverSocket);
            }
        }

        protected void AcceptAsyncCallback(IAsyncResult ar)
        {
            Socket socket = (Socket)ar.AsyncState;
            //if (socket.Connected == false) return;
            if (socket == null) return;
            try
            {
                Socket client = socket.EndAccept(ar);
                m_clientSocket = client;
                m_clientSocket.SendTimeout = 1000;
                m_clientSocket.ReceiveTimeout = 1000 * 10;
                Hynn.Common.Logger.Log.Logging(System.Reflection.MethodBase.GetCurrentMethod()).Debug
                    ("下位机请求连接," + m_clientSocket.LocalEndPoint.ToString());                
            }
            catch (Exception ex)
            {
                Hynn.Common.Logger.Log.Logging(System.Reflection.MethodBase.GetCurrentMethod())
                    .Info("当前域发生没有处理的异常,客户端连接", ex);
            }
            finally
            {
                try
                {
                    this.m_serverSocket.BeginAccept(AcceptAsyncCallback, m_serverSocket);
                }
                catch (Exception exception)
                {

                }
            }
        }

        public bool Send(byte[] data, int length)
        {
            if (m_clientSocket == null || m_clientSocket.Connected == false)
                return false;

            try
            {
                int sendLength = m_clientSocket.Send(data, length, SocketFlags.None);
                System.Threading.Thread.Sleep(10);

                return sendLength == length;
            }
            catch (System.Net.Sockets.SocketException)
            {
                return false;
            }
        }

        public Tuple<bool, byte[]> ReSendAndCallback(byte[] data, int length)
        {   
            Tuple<bool, byte[]> result = new Tuple<bool,byte[]>(false, null);
            int connectCount;
            for (int i = 0; i < 3; i++)
            {
                connectCount = 0;
                while(connectCount++ < 10)
                {
                    if (this.m_clientSocket == null || this.m_clientSocket.Connected == false)
                    {
                        Thread.Sleep(500);
                    }
                    else
                    {
                        break;
                    }
                }
                
                result = SendAndCallback(data, length);
                if (result.Item1 == true)
                {
                    break;
                }
                else
                {
                    Hynn.Common.Logger.Log.Logging(System.Reflection.MethodBase.GetCurrentMethod()).Debug(String.Format("第{0}次指令发送失败", i + 1));
                }
            }
            if (result.Item1 == false)
            {
                Hynn.Common.Logger.Log.Logging(System.Reflection.MethodBase.GetCurrentMethod()).Debug(String.Format("三次发送指令失败！"));
            }
            return result;
        }


        private Tuple<bool, byte[]> SendAndCallback(byte[] data, int length)
        {
            if (m_clientSocket == null || m_clientSocket.Connected == false)
                return new Tuple<bool, byte[]>(false, null);

            lock (obj)
            {
                try
                {
                    if (this.WriteCommunicationLog)
                    {
                        this.m_time.Restart();
                        Hynn.Common.Logger.Log.Logging(System.Reflection.MethodBase.GetCurrentMethod())
    .Debug(String.Format("发送当前命令字：{0}", Convert.ToChar(data[7])));
                    }

                    if (m_clientSocket != null)
                    {
                        int SendLength = m_clientSocket.Send(data, length, SocketFlags.None);
                    }
                    System.Threading.Thread.Sleep(100);

                    byte[] receive = new byte[2048];
                    int receiveLength = ReceiveData(ref receive, SocketFlags.None);

                    if (receiveLength <= 0)
                    {
                        return new Tuple<bool, byte[]>(false, null);
                    }

                    byte[] temp = new byte[receiveLength];
                    Array.Copy(receive, 0, temp, 0, temp.Length);
                    return new Tuple<bool, byte[]>(true, temp);
                }
                catch (System.Net.Sockets.SocketException)
                {
                    if (m_clientSocket != null)
                    {
                        //m_clientSocket.Shutdown(SocketShutdown.Both);
                        m_clientSocket.Close();
                        m_clientSocket = null;
                    }
                    return new Tuple<bool, byte[]>(false, null);
                }
            }      
        }

        public int ReceiveData(ref byte[] data, SocketFlags flag)
        {
            try
            {
                //if (this.m_serverSocket != null)
                if (this.m_clientSocket != null
                    && this.m_clientSocket.Connected == true
                    /*&& this.m_clientSocket.Poll(-1, SelectMode.SelectRead)*/)
                {
                    int receiveLength = this.m_clientSocket.Receive(data, flag);
                    if (receiveLength < 0)
                    {
                        this.m_clientSocket.Close();
                        this.m_clientSocket = null;
                        System.Threading.Thread.Sleep(100);
                        Hynn.Common.Logger.Log.Logging(System.Reflection.MethodBase.GetCurrentMethod()).Debug(
                            "下位机断开网络连接");
                    }
                    if (data[0] == 0xEE && data[1] == 0xEE && data[2] == 0xEE && data[3] == 0xEE)
                    {
                        return receiveLength;
                    }
                    return -1;
                }
                else
                {  
                    return -1;
                }
            }
            catch (System.ObjectDisposedException objectDisposedException)
            {
                this.m_clientSocket = null;
                Hynn.Common.Logger.Log.Logging(System.Reflection.MethodBase.GetCurrentMethod())
                    .Error("当前域发生没有处理的异常,当前Socket已被释放", objectDisposedException);
                return -1;
            }
            catch (Exception ex)
            {
                if (this.WriteCommunicationLog)
                {
                    this.m_time.Stop();
                    Hynn.Common.Logger.Log.Logging(System.Reflection.MethodBase.GetCurrentMethod())
                        .Debug(Convert.ToInt32(this.m_time.ElapsedMilliseconds).ToString() + String.Format("接受当前命令字：{0}", Convert.ToChar(data[7])));
                }

                Hynn.Common.Logger.Log.Logging(System.Reflection.MethodBase.GetCurrentMethod())
                    .Warn("当前域发生没有处理的异常", ex);
                try
                {
                    if (m_clientSocket != null)
                    {
                        m_clientSocket.Close();
                        m_clientSocket = null;
                    }
                }
                catch (Exception sockex)
                {
                    Hynn.Common.Logger.Log.Logging(System.Reflection.MethodBase.GetCurrentMethod())
                        .Warn("当前域发生没有处理的异常,关闭本地Socket再Init", sockex);
                }
                return -1;
            }   
        }

        public void Close()
        {
            if (m_clientSocket != null)
            {
                m_clientSocket.Close();
                m_clientSocket = null;
            }
            if (m_serverSocket != null)
            {
                m_serverSocket.Close();
                m_serverSocket = null;
            }
            
        }
    }
}
