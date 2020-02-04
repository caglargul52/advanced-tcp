using AdvancedTCP;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace ExampleClient
{
    class Program
    {
        private static Client _client;

        static void Main(string[] args)
        {
            _client = new Client();
            _client.ConnectedServer += _client_ConnectedServer;
            _client.DisconnectedServer += _client_DisconnectedServer;
            _client.ReceivedMessageFromServer += _client_ReceivedMessageFromServer;

            _client.ServerIpAddress = IPAddress.Parse("127.0.0.1");
            _client.ServerPort = 9000;
            _client.ConnectServer();

            Console.Read();
        }

        private static void _client_ReceivedMessageFromServer(string message)
        {
            Console.WriteLine(message);
        }

        private static void _client_DisconnectedServer(string message)
        {
            Console.WriteLine(message);

        }

        private static void _client_ConnectedServer(LocalInfo info)
        {
            Console.WriteLine(info.pcName + " - " + info.ipAddress + ":" + info.localEndPoint + " Message: Connected to Server");
            _client.SendMessageToServer("Hello");
        }
    }
}
