using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using MCListener.TestTool.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MCListener.TestTool
{
    public interface IMulticastClient
    {
        void StartListening(Action<string> callback);
        void SendMessage(string message);
    }

    public class MulticastClient : IMulticastClient
    {
        
        private ILogger<MulticastClient> logger;
        MulticastConfiguration configuration;

        public MulticastClient(IOptions<Configuration.MulticastConfiguration> configuration, ILogger<MulticastClient> logger)
        {
            this.configuration = configuration.Value;
            this.configuration.AssertValidity();

            this.logger = logger;
            logger.LogDebug($"Starting client: {this.configuration.Ip}:{this.configuration.Port}");
        }

        public void StartListening(Action<string> callback)
        {
            new Thread(() =>{
                while (true)
                {
                    try
                    {
                        logger.LogDebug($"Start listener for multicat {configuration.Ip}:{configuration.Port}");
                        var mcastSocket = GetMulticastSocket();


                        //Do binding here (only on reading port)
                        IPEndPoint localEP = new IPEndPoint(IPAddress.Any, configuration.Port);
                        mcastSocket.Bind(localEP);

                        byte[] arr = new byte[4096];

                        while (true)
                        {
                            var receivedBytes = mcastSocket.Receive(arr);
                            logger.LogDebug($"{configuration.Ip}:{configuration.Port}.Received -> {receivedBytes} bytes");

                            var str = System.Text.Encoding.ASCII.GetString(arr).Substring(0, receivedBytes);
                            logger.LogDebug($"{configuration.Ip}:{configuration.Port}.Received -> {str}");

                            callback(str);
                        }
                    }
                    catch (Exception e)
                    {
                        logger.LogError($"Multicast failed: {e.ToString()}, going for a retry iun 1 second");
                        Thread.Sleep(1000); //wait and try again
                    }
                }
            }).Start();
        }


        public void SendMessage(string message)
        {
            Action sendAction = () =>
            {
                try
                {
                    var s = GetMulticastSocket();

                    logger.LogDebug($"{configuration.Ip}:{configuration.Port}.Write -> {message}");

                    s.Connect(new IPEndPoint(IPAddress.Parse(configuration.Ip), configuration.Port)); //We need to explicitly connect to the port before sending
                    var b = System.Text.Encoding.ASCII.GetBytes(message);
                    s.Send(b, b.Length, SocketFlags.None);
                    s.Close();
                }
                catch (Exception e)
                {
                    logger.LogWarning($"Cannot transmit message {message}: {e.Message}");
                }
            };

            //Run once (in line, no performance issues expected
            if (!configuration.PerformBurst) { sendAction(); return; }
            
            //Start a background burst (and direcly resume to prevent locking here
            Thread burstAction = new Thread(() =>
            {
                for (int i = 0; i < configuration.BurstCount; i++)
                {
                    logger.LogTrace($"Bursting {i}/{configuration.BurstCount}");
                    sendAction();
                    Thread.Sleep(configuration.BurstIntervalMS);
                }
            });
            burstAction.Start();
        }

        private Socket GetMulticastSocket()
        {
            var mcastSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            IPAddress mcastAddress = IPAddress.Parse(configuration.Ip);                
            

            MulticastOption option = new MulticastOption(mcastAddress, IPAddress.Any);              
            mcastSocket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, option);
            mcastSocket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastLoopback, false);
            mcastSocket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 100);

            //EndPoint remoteEp = new IPEndPoint(IPAddress.Any, port);
            return mcastSocket;
        }
    }
}