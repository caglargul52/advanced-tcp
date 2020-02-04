using AdvancedTCP;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace ExampleServer
{
    class Program
    {
        private static Server _server;

        static void Main(string[] args)
        {
            try
            {
                _server = new Server();

                _server.ReceivedMessageFromClient += Server_ReceivedMessageFromClient;
                _server.ConnectedClient += Server_ConnectedClient;
                _server.DisconnectedClient += Server_DisconnectedClient;

                _server.IpAddress = IPAddress.Parse("127.0.0.1");
                _server.Port = 9000;

                //The following computers will be thrown directly from the server when connected.
                _server.BlockedIpList = new List<IPAddress> { IPAddress.Parse("192.168.1.5"), IPAddress.Parse("192.168.1.6") };
                bool isOpened = _server.StartServer();

                if (isOpened) Console.WriteLine(_server.IpAddress + ":" + _server.Port + " - Server started...");
                else Console.WriteLine("Failed to start server!");

                Console.ReadLine();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        private static void Server_DisconnectedClient(ClientInfo clientInfo)
        {
            Console.Clear();
            foreach (var client in _server.GetClientsList())
            {
                Console.WriteLine(client.pcName + " - " + client.ipAndRemoteEndPoint);
            }
        }

        private static void Server_ConnectedClient(ClientInfo clientInfo)
        {
            Console.Clear();
            foreach (var client in _server.GetClientsList())
            {
                Console.WriteLine(client.pcName + " - " + client.ipAndRemoteEndPoint);
            }

            _server.SendMessage(clientInfo, "Welcome to Server");
        }

        private static void Server_ReceivedMessageFromClient(ClientInfo clientInfo, string message)
        {
            Console.WriteLine(clientInfo.pcName + " - " + clientInfo.ipAndRemoteEndPoint + " -> " + message);
        }
    }
}
