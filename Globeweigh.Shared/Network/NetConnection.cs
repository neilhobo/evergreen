using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Globeweigh.Shared.Network
{
    public class NetConnection
    {
        public delegate void NetConnectionEventHandler<TEventArgs>(object sender, NetConnection connection, TEventArgs e);
        public delegate void NetConnectionEventHandler(object sender, NetConnection connection);

        private TcpClient client;
        //        private TcpListener listener;
        private List<Task> tasks = new List<Task>();

        public event NetConnectionEventHandler<byte[]> OnDataReceived;

        public int BufferSize = 1024;

        public EndPoint RemoteEndPoint
        {
            get
            {
                if (client == null)
                    throw new InvalidOperationException("Client is not connected");
                return client.Client.RemoteEndPoint;
            }
        }

        public NetConnection()
        {

        }

        public bool Connect(IPAddress address, int port)
        {
            client = new TcpClient();
            var result = client.BeginConnect(address, port, null, null);
            var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(1));
            if (!success)
            {
                return false;
            }
            //            client.Connect(address, port);
            StartReceiveFrom(this);
            return true;
        }

        public void Disconnect()
        {
            if (client.Client.Connected)
                client.Client.Disconnect(false);
        }

        public void Send(byte[] data)
        {
            client.GetStream().Write(data, 0, data.Length);
        }

        public void Send(string message)
        {
            StreamWriter writer = new StreamWriter(client.GetStream(), Encoding.ASCII);
            writer.Write(message);
        }

        private void CallOnDataReceived(NetConnection connection, byte[] data)
        {
            if (OnDataReceived != null)
                OnDataReceived(this, connection, data);
        }

        //        public bool IsConnected()
        //        {
        //            return client.Connected;
        //        }

        public async Task<bool> IsPingable(string ipAddress)
        {
            bool pingable = false;
            Ping pinger = new Ping();
            try
            {
                PingReply reply = await pinger.SendPingAsync(ipAddress);
                pingable = reply.Status == IPStatus.Success;
            }
            catch (PingException)
            {
            }
            try
            {
                if (client.Client.Connected && !pingable)
                {
                    Disconnect();
                }
                //                if (!client.Client.Connected && pingable)
                //                {
                //                    return false;
                //                }
            }
            catch (Exception ex)
            {
                var test = string.Empty;
            }

            return pingable;
        }

        //
        private void StartReceiveFrom(NetConnection client)
        {
            tasks.Add(ReceiveFromAsync(client));
        }
        private async Task ReceiveFromAsync(NetConnection client)
        {
            try
            {
                NetworkStream stream = client.client.GetStream();
                byte[] buffer = new byte[BufferSize];
                MemoryStream ms = new MemoryStream();
                while (client.client.Connected)
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    ms.Write(buffer, 0, bytesRead);
                    if (!stream.DataAvailable)
                    {
                        CallOnDataReceived(client, ms.ToArray());
                        ms.Seek(0, SeekOrigin.Begin);
                        ms.SetLength(0);
                    }
                }
                ms.Close();
                client.client.Close();
                Console.WriteLine("Close Called");
            }
            catch
            {
                //                CallOnDisconnect(client);
                throw;
            }
        }
    }
}
