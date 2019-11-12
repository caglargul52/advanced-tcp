
# .Net C# Advanced TCP Library

## Features
 - End-to-end encrypted messaging (AES Algorithm)
 - Sending messages to all clients at the same time
 - Block computers with unwanted ip addresses
 - Automatic connection of clients when internet connection is disconnected and reconnect.
 - Asynchronous communication
 - A Server / MultiClient

## Quick Start
### Client.cs
        static void Main(string[] args)
        {
            Client _client = new Client("password");
            _client.ConnectedServer += _client_ConnectedServer;
            _client.DisconnectedServer += _client_DisconnectedServer;
            _client.ReceivedMessageFromServer += _client_ReceivedMessageFromServer;
            _client.ServerIpAddress = IPAddress.Parse("127.0.0.1");
            _client.ServerPort = 9000;
            _client.ConnectServer();
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
            Console.WriteLine(info.pcName + " - " + info.ipAddress + ":" + info.localEndPoint + 	  " Message: Connected to Server");
            _client.SendMessageToServer("Hello");
        }

### Server.cs
        static void Main(string[] args)
        {
	            Server _server = new Server("password");
                _server.ReceivedMessageFromClient += Server_ReceivedMessageFromClient;
                _server.ConnectedClient += Server_ConnectedClient;
                _server.DisconnectedClient += Server_DisconnectedClient;
                _server.IpAddress = IPAddress.Parse("127.0.0.1");
                _server.Port = 9000;

                //The following computers will be thrown directly from the server when connected.
                _server.BlockedIpList = new List<IPAddress> { IPAddress.Parse("192.168.1.5"), IPAddress.Parse("192.168.1.6") };
                _server.StartServer();
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


