using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AdvancedTCP
{
    public delegate void ServerConnectedEventHandler(LocalInfo info);
    public delegate void ServerDisconnectedEventHandler(string message);
    public delegate void ReceivedMessageFromServerEventHandler(string message);

    public struct LocalInfo
    {
        public string ipAddress;
        public int localEndPoint;
        public string pcName;
    }
    public class Client
    {
        public event ServerConnectedEventHandler ConnectedServer;
        public event ServerDisconnectedEventHandler DisconnectedServer;
        public event ReceivedMessageFromServerEventHandler ReceivedMessageFromServer;

        private Tcp_Client _client;
        private Thread _threadInternetControlListener;
        private Thread _threadListen;
        private bool _isServerConnected;
        private bool _flagOfThreadsBreak = true;
        private string _key;
        public IPAddress ServerIpAddress { get; set; }
        public int ServerPort { get; set; }

        public Client()
        {
            _key = null;
            _threadListen = new Thread(ServerListen);
            _threadListen.IsBackground = true;
            _threadListen.Start();

            _threadInternetControlListener = new Thread(InternetListen);
            _threadInternetControlListener.IsBackground = true;
            _threadInternetControlListener.Start();
        }

        public Client(string key)
        {
            _key = key;
            _threadListen = new Thread(ServerListen);
            _threadListen.IsBackground = true;
            _threadListen.Start();

            _threadInternetControlListener = new Thread(InternetListen);
            _threadInternetControlListener.IsBackground = true;
            _threadInternetControlListener.Start();
        }

        public void ConnectServer()
        {
            if (_client != null)
            {
                throw new Exception("The server has already been started.");
            }

            StartClient();
            _flagOfThreadsBreak = false;
        }

        public void DisconnectServer()
        {
            _flagOfThreadsBreak = true;
            if (_client != null)
            {
                _client.Dispose();
                _client = null;
                _isServerConnected = false;
                DisconnectedServer?.Invoke("No server connection.");
            }

        }

        public bool SendMessageToServer(string message)
        {
            try
            {
                MessageModel m = new MessageModel();
                m.Type = Type.Message;
                m.Message = message;
                m.PcName = Environment.MachineName;
                string json = JsonConvert.SerializeObject(m);

                if (_key == null)
                {
                    return _client.Send(Encoding.UTF8.GetBytes(json));
                }

                return _client.Send(ExtensionMethods.Encrypt(json, _key));
            }
            catch
            {
                return false;
            }
        }

        private void StartClient()
        {
            try
            {
                _client = new Tcp_Client(ServerIpAddress.ToString(), ServerPort, ServerConnected, ServerDisconnected, MessageReceived, false);

                _isServerConnected = true;

                MessageModel message = new MessageModel();
                message.Type = Type.ClientLogon;
                message.Message = string.Empty;
                message.PcName = Environment.MachineName;

                string json = JsonConvert.SerializeObject(message);

                if (_key == null)
                {
                    _client.Send(Encoding.UTF8.GetBytes(json));
                }
                else
                {
                    _client.Send(ExtensionMethods.Encrypt(json, _key));
                }

            }
            catch
            {
                _isServerConnected = false;
                DisconnectedServer?.Invoke("No server connection");
            }
        }

        private void ServerListen()
        {
            while (true)
            {
                if (!_flagOfThreadsBreak)
                {
                    if (!_isServerConnected)
                    {
                        StartClient();
                    }
                }

                Thread.Sleep(1000);
            }
        }

        private void InternetListen()
        {
            while (true)
            {
                if (!_flagOfThreadsBreak)
                {
                    try
                    {
                        bool status = _client != null && _client.Send(new byte[] { });

                        if (!status)
                        {
                            if (_client != null)
                            {
                                _client.Dispose();
                                _client = null;
                            }
                            _isServerConnected = false;
                        }
                    }
                    catch
                    {
                        if (_client != null)
                        {
                            _client.Dispose();
                            _client = null;
                        }
                        _isServerConnected = false;
                    }
                }

                Thread.Sleep(1000);
            }
        }

        private bool MessageReceived(byte[] data)
        {
            string receiveData = string.Empty;

            if (_key == null)
            {
                 receiveData = Encoding.UTF8.GetString(data);
            }
            else
            {
                receiveData = Encoding.UTF8.GetString(ExtensionMethods.Decrypt(Convert.ToBase64String(data), _key));
            }


            var message = JsonConvert.DeserializeObject<MessageModel>(receiveData);

            if (message == null) return true;

            LocalInfo info;
            info.ipAddress = _client.LocalAddress;
            info.localEndPoint = Convert.ToInt32(_client.LocalPort);
            info.pcName = Environment.MachineName;

            if (message.Type == Type.AcceptLogon)
            {
                _isServerConnected = true;
                ConnectedServer?.Invoke(info);
            }

            if (message.Type == Type.Disconnect)
            {
                _client.Dispose();
                _client = null;
                _flagOfThreadsBreak = true;
                _isServerConnected = false;
                DisconnectedServer?.Invoke("Connection disconnected by Server");
            }

            if (message.Type == Type.Blocked)
            {
                _client.Dispose();
                _client = null;
                _isServerConnected = false;
                DisconnectedServer?.Invoke("It does not connect to the server because it is a blocked IP.");
            }

            if (message.Type == Type.Message)
            {
                ReceivedMessageFromServer?.Invoke(message.Message);
            }

            return true;
        }

        private bool ServerConnected()
        {
            return true;
        }

        private bool ServerDisconnected()
        {
            _client.Dispose();
            _client = null;
            _isServerConnected = false;
            return true;
        }
    }

    internal class Tcp_Client : IDisposable
    {
        #region Public-Members
        public string LocalAddress { get; set; }
        public string LocalPort { get; set; }
        #endregion

        #region Private-Members

        private bool _Disposed = false;

        private string _SourceIp;
        private int _SourcePort;
        private string _ServerIp;
        private int _ServerPort;
        private bool _Debug;
        private TcpClient _Client;
        private bool _Connected;
        private Func<byte[], bool> _MessageReceived = null;
        private Func<bool> _ServerConnected = null;
        private Func<bool> _ServerDisconnected = null;

        private readonly SemaphoreSlim _SendLock;
        private CancellationTokenSource _TokenSource;
        private CancellationToken _Token;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize the TCP client.
        /// </summary>
        /// <param name="serverIp">The IP address or hostname of the server.</param>
        /// <param name="serverPort">The TCP port on which the server is listening.</param>
        /// <param name="serverConnected">Function to be called when the server connects.</param>
        /// <param name="serverDisconnected">Function to be called when the connection is severed.</param>
        /// <param name="messageReceived">Function to be called when a message is received.</param>
        /// <param name="debug">Enable or debug logging messages.</param>
        public Tcp_Client(
            string serverIp,
            int serverPort,
            Func<bool> serverConnected,
            Func<bool> serverDisconnected,
            Func<byte[], bool> messageReceived,
            bool debug)
        {
            if (String.IsNullOrEmpty(serverIp))
            {
                throw new ArgumentNullException(nameof(serverIp));
            }

            if (serverPort < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(serverPort));
            }

            _ServerIp = serverIp;
            _ServerPort = serverPort;

            _ServerConnected = serverConnected;
            _ServerDisconnected = serverDisconnected;
            _MessageReceived = messageReceived ?? throw new ArgumentNullException(nameof(messageReceived));

            _Debug = debug;

            _SendLock = new SemaphoreSlim(1);

            _Client = new TcpClient();
            IAsyncResult ar = _Client.BeginConnect(_ServerIp, _ServerPort, null, null);
            WaitHandle wh = ar.AsyncWaitHandle;

            try
            {
                if (!ar.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(5), false))
                {
                    _Client.Close();
                    throw new TimeoutException("Timeout connecting to " + _ServerIp + ":" + _ServerPort);
                }

                _Client.EndConnect(ar);

                _SourceIp = ((IPEndPoint)_Client.Client.LocalEndPoint).Address.ToString();
                _SourcePort = ((IPEndPoint)_Client.Client.LocalEndPoint).Port;
                LocalAddress = ((IPEndPoint)_Client.Client.LocalEndPoint).Address.ToString();
                LocalPort = ((IPEndPoint)_Client.Client.LocalEndPoint).Port.ToString();
                _Connected = true;
            }
            catch (Exception)
            {
                throw;
            }
            finally
            {
                wh.Close();
            }

            if (_ServerConnected != null)
            {
                Task.Run(() => _ServerConnected());
            }

            _TokenSource = new CancellationTokenSource();
            _Token = _TokenSource.Token;
            Task.Run(async () => await DataReceiver(_Token), _Token);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Tear down the client and dispose of background workers.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Send data to the server.
        /// </summary>
        /// <param name="data">Byte array containing data.</param>
        /// <returns>Boolean indicating if the message was sent successfully.</returns>
        public bool Send(byte[] data)
        {
            return MessageWrite(data);
        }

        /// <summary>
        /// Send data to the server asynchronously
        /// </summary>
        /// <param name="data">Byte array containing data.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public async Task<bool> SendAsync(byte[] data)
        {
            return await MessageWriteAsync(data);
        }

        /// <summary>
        /// Determine whether or not the client is connected to the server.
        /// </summary>
        /// <returns>Boolean indicating if the client is connected to the server.</returns>
        public bool IsConnected()
        {
            return _Connected;
        }

        #endregion

        #region Private-Methods

        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed)
            {
                return;
            }

            if (disposing)
            {
                if (_Client != null)
                {
                    if (_Client.Connected)
                    {
                        NetworkStream ns = _Client.GetStream();
                        if (ns != null)
                        {
                            ns.Close();
                        }
                    }

                    _Client.Close();
                }

                _TokenSource.Cancel();
                _TokenSource.Dispose();

                _SendLock.Dispose();

                _Connected = false;
            }

            _Disposed = true;
        }

        private void Log(string msg)
        {
            if (_Debug)
            {
                Console.WriteLine(msg);
            }
        }

        private void LogException(string method, Exception e)
        {
            Log("================================================================================");
            Log(" = Method: " + method);
            Log(" = Exception Type: " + e.GetType().ToString());
            Log(" = Exception Data: " + e.Data);
            Log(" = Inner Exception: " + e.InnerException);
            Log(" = Exception Message: " + e.Message);
            Log(" = Exception Source: " + e.Source);
            Log(" = Exception StackTrace: " + e.StackTrace);
            Log("================================================================================");
        }

        private string BytesToHex(byte[] data)
        {
            if (data == null || data.Length < 1)
            {
                return "(null)";
            }

            return BitConverter.ToString(data).Replace("-", "");
        }

        private async Task DataReceiver(CancellationToken? cancelToken = null)
        {
            try
            {
                #region Wait-for-Data

                while (true)
                {
                    cancelToken?.ThrowIfCancellationRequested();

                    #region Check-if-Client-Connected-to-Server

                    if (_Client == null)
                    {
                        Log("*** DataReceiver null TCP interface detected, disconnection or close assumed");
                        break;
                    }

                    if (!_Client.Connected)
                    {
                        Log("*** DataReceiver server disconnected");
                        break;
                    }

                    #endregion

                    #region Read-Message-and-Handle

                    byte[] data = await MessageReadAsync();
                    if (data == null)
                    {
                        await Task.Delay(30);
                        continue;
                    }

                    Task<bool> unawaited = Task.Run(() => _MessageReceived(data));

                    #endregion
                }

                #endregion
            }
            catch (OperationCanceledException)
            {
                throw; // normal cancellation
            }
            catch (Exception)
            {
                Log("*** DataReceiver server disconnected");
            }
            finally
            {
                _Connected = false;
                _ServerDisconnected?.Invoke();
            }
        }

        private byte[] MessageRead()
        {
            string sourceIp = "";
            int sourcePort = 0;

            try
            {
                #region Check-for-Null-Values

                if (_Client == null)
                {
                    Log("*** MessageRead null client supplied");
                    return null;
                }

                if (!_Client.Connected)
                {
                    Log("*** MessageRead supplied client is not connected");
                    return null;
                }

                #endregion

                #region Variables

                int bytesRead = 0;
                int sleepInterval = 25;
                int maxTimeout = 500;
                int currentTimeout = 0;
                bool timeout = false;

                sourceIp = ((IPEndPoint)_Client.Client.RemoteEndPoint).Address.ToString();
                sourcePort = ((IPEndPoint)_Client.Client.RemoteEndPoint).Port;
                NetworkStream ClientStream = null;

                try
                {
                    ClientStream = _Client.GetStream();
                }
                catch (Exception e)
                {
                    Log("*** MessageRead disconnected while attaching to stream: " + e.Message);
                    return null;
                }

                byte[] headerBytes;
                string header = "";
                long contentLength;
                byte[] contentBytes;

                #endregion

                #region Read-Header

                if (!ClientStream.CanRead && !ClientStream.DataAvailable)
                {
                    return null;
                }

                using (MemoryStream headerMs = new MemoryStream())
                {
                    #region Read-Header-Bytes

                    byte[] headerBuffer = new byte[1];
                    timeout = false;
                    currentTimeout = 0;
                    int read = 0;

                    while ((read = ClientStream.ReadAsync(headerBuffer, 0, headerBuffer.Length).Result) > 0)
                    {
                        if (read > 0)
                        {
                            headerMs.Write(headerBuffer, 0, read);
                            bytesRead += read;
                            currentTimeout = 0;

                            if (bytesRead > 1)
                            {
                                // check if end of headers reached
                                if (headerBuffer[0] == 58)
                                {
                                    break;
                                }
                            }
                        }
                        else
                        {
                            if (currentTimeout >= maxTimeout)
                            {
                                timeout = true;
                                break;
                            }
                            else
                            {
                                currentTimeout += sleepInterval;
                                Task.Delay(sleepInterval).Wait();
                            }
                        }
                    }

                    if (timeout)
                    {
                        Log("*** MessageRead timeout " + currentTimeout + "ms/" + maxTimeout + "ms exceeded while reading header after reading " + bytesRead + " bytes");
                        return null;
                    }

                    headerBytes = headerMs.ToArray();
                    if (headerBytes == null || headerBytes.Length < 1)
                    {
                        return null;
                    }

                    #endregion

                    #region Process-Header

                    header = Encoding.UTF8.GetString(headerBytes);
                    header = header.Replace(":", "");

                    if (!Int64.TryParse(header, out contentLength))
                    {
                        Log("*** MessageRead malformed message from server (message header not an integer)");
                        return null;
                    }

                    #endregion
                }

                #endregion

                #region Read-Data

                using (MemoryStream dataMs = new MemoryStream())
                {
                    long bytesRemaining = contentLength;
                    timeout = false;
                    currentTimeout = 0;

                    int read = 0;
                    byte[] buffer;
                    long bufferSize = 2048;
                    if (bufferSize > bytesRemaining)
                    {
                        bufferSize = bytesRemaining;
                    }

                    buffer = new byte[bufferSize];

                    while ((read = ClientStream.ReadAsync(buffer, 0, buffer.Length).Result) > 0)
                    {
                        if (read > 0)
                        {
                            dataMs.Write(buffer, 0, read);
                            bytesRead = bytesRead + read;
                            bytesRemaining = bytesRemaining - read;

                            // reduce buffer size if number of bytes remaining is
                            // less than the pre-defined buffer size of 2KB
                            if (bytesRemaining < bufferSize)
                            {
                                bufferSize = bytesRemaining;
                                // Console.WriteLine("Adjusting buffer size to " + bytesRemaining);
                            }

                            buffer = new byte[bufferSize];

                            // check if read fully
                            if (bytesRemaining == 0)
                            {
                                break;
                            }

                            if (bytesRead == contentLength)
                            {
                                break;
                            }
                        }
                        else
                        {
                            if (currentTimeout >= maxTimeout)
                            {
                                timeout = true;
                                break;
                            }
                            else
                            {
                                currentTimeout += sleepInterval;
                                Task.Delay(sleepInterval).Wait();
                            }
                        }
                    }

                    if (timeout)
                    {
                        Log("*** MessageRead timeout " + currentTimeout + "ms/" + maxTimeout + "ms exceeded while reading content after reading " + bytesRead + " bytes");
                        return null;
                    }

                    contentBytes = dataMs.ToArray();
                }

                #endregion

                #region Check-Content-Bytes

                if (contentBytes == null || contentBytes.Length < 1)
                {
                    Log("*** MessageRead no content read");
                    return null;
                }

                if (contentBytes.Length != contentLength)
                {
                    Log("*** MessageRead content length " + contentBytes.Length + " bytes does not match header value of " + contentLength);
                    return null;
                }

                #endregion

                return contentBytes;
            }
            catch (Exception)
            {
                Log("*** MessageRead server disconnected");
                return null;
            }
        }

        private async Task<byte[]> MessageReadAsync()
        {
            string sourceIp = "";
            int sourcePort = 0;

            try
            {
                #region Check-for-Null-Values

                if (_Client == null)
                {
                    Log("*** MessageReadAsync null client supplied");
                    return null;
                }

                if (!_Client.Connected)
                {
                    Log("*** MessageReadAsync supplied client is not connected");
                    return null;
                }

                #endregion

                #region Variables

                int bytesRead = 0;
                int sleepInterval = 25;
                int maxTimeout = 500;
                int currentTimeout = 0;
                bool timeout = false;

                sourceIp = ((IPEndPoint)_Client.Client.RemoteEndPoint).Address.ToString();
                sourcePort = ((IPEndPoint)_Client.Client.RemoteEndPoint).Port;
                LocalAddress = ((IPEndPoint)_Client.Client.LocalEndPoint).Address.ToString();
                LocalPort = ((IPEndPoint)_Client.Client.LocalEndPoint).Port.ToString();
                NetworkStream ClientStream = null;

                try
                {
                    ClientStream = _Client.GetStream();
                }
                catch (Exception e)
                {
                    Log("*** MessageRead disconnected while attaching to stream: " + e.Message);
                    return null;
                }

                byte[] headerBytes;
                string header = "";
                long contentLength;
                byte[] contentBytes;

                #endregion

                #region Read-Header

                if (!ClientStream.CanRead && !ClientStream.DataAvailable)
                {
                    return null;
                }

                using (MemoryStream headerMs = new MemoryStream())
                {
                    #region Read-Header-Bytes

                    byte[] headerBuffer = new byte[1];
                    timeout = false;
                    currentTimeout = 0;
                    int read = 0;

                    while ((read = await ClientStream.ReadAsync(headerBuffer, 0, headerBuffer.Length)) > 0)
                    {
                        if (read > 0)
                        {
                            await headerMs.WriteAsync(headerBuffer, 0, read);
                            bytesRead += read;
                            currentTimeout = 0;

                            if (bytesRead > 1)
                            {
                                // check if end of headers reached
                                if (headerBuffer[0] == 58)
                                {
                                    break;
                                }
                            }
                        }
                        else
                        {
                            if (currentTimeout >= maxTimeout)
                            {
                                timeout = true;
                                break;
                            }
                            else
                            {
                                currentTimeout += sleepInterval;
                                await Task.Delay(sleepInterval);
                            }
                        }
                    }

                    if (timeout)
                    {
                        Log("*** MessageRead timeout " + currentTimeout + "ms/" + maxTimeout + "ms exceeded while reading header after reading " + bytesRead + " bytes");
                        return null;
                    }

                    headerBytes = headerMs.ToArray();
                    if (headerBytes == null || headerBytes.Length < 1)
                    {
                        return null;
                    }

                    #endregion

                    #region Process-Header

                    header = Encoding.UTF8.GetString(headerBytes);
                    header = header.Replace(":", "");

                    if (!Int64.TryParse(header, out contentLength))
                    {
                        Log("*** MessageRead malformed message from server (message header not an integer)");
                        return null;
                    }

                    #endregion
                }

                #endregion

                #region Read-Data

                using (MemoryStream dataMs = new MemoryStream())
                {
                    long bytesRemaining = contentLength;
                    timeout = false;
                    currentTimeout = 0;

                    int read = 0;
                    byte[] buffer;
                    long bufferSize = 2048;
                    if (bufferSize > bytesRemaining)
                    {
                        bufferSize = bytesRemaining;
                    }

                    buffer = new byte[bufferSize];

                    while ((read = await ClientStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        if (read > 0)
                        {
                            await dataMs.WriteAsync(buffer, 0, read);
                            bytesRead = bytesRead + read;
                            bytesRemaining = bytesRemaining - read;

                            // reduce buffer size if number of bytes remaining is
                            // less than the pre-defined buffer size of 2KB
                            if (bytesRemaining < bufferSize)
                            {
                                bufferSize = bytesRemaining;
                            }

                            buffer = new byte[bufferSize];

                            // check if read fully
                            if (bytesRemaining == 0)
                            {
                                break;
                            }

                            if (bytesRead == contentLength)
                            {
                                break;
                            }
                        }
                        else
                        {
                            if (currentTimeout >= maxTimeout)
                            {
                                timeout = true;
                                break;
                            }
                            else
                            {
                                currentTimeout += sleepInterval;
                                await Task.Delay(sleepInterval);
                            }
                        }
                    }

                    if (timeout)
                    {
                        Log("*** MessageReadAsync timeout " + currentTimeout + "ms/" + maxTimeout + "ms exceeded while reading content after reading " + bytesRead + " bytes");
                        return null;
                    }

                    contentBytes = dataMs.ToArray();
                }

                #endregion

                #region Check-Content-Bytes

                if (contentBytes == null || contentBytes.Length < 1)
                {
                    Log("*** MessageRead no content read");
                    return null;
                }

                if (contentBytes.Length != contentLength)
                {
                    Log("*** MessageRead content length " + contentBytes.Length + " bytes does not match header value of " + contentLength);
                    return null;
                }

                #endregion

                return contentBytes;
            }
            catch (Exception)
            {
                Log("*** MessageRead server disconnected");
                return null;
            }
        }

        private bool MessageWrite(byte[] data)
        {
            bool disconnectDetected = false;

            try
            {
                #region Check-if-Connected

                if (_Client == null)
                {
                    Log("MessageWrite client is null");
                    disconnectDetected = true;
                    return false;
                }

                #endregion

                #region Format-Message

                string header = "";
                byte[] headerBytes;
                byte[] message;

                if (data == null || data.Length < 1)
                {
                    header += "0:";
                }
                else
                {
                    header += data.Length + ":";
                }

                headerBytes = Encoding.UTF8.GetBytes(header);
                int messageLen = headerBytes.Length;
                if (data != null && data.Length > 0)
                {
                    messageLen += data.Length;
                }

                message = new byte[messageLen];
                Buffer.BlockCopy(headerBytes, 0, message, 0, headerBytes.Length);

                if (data != null && data.Length > 0)
                {
                    Buffer.BlockCopy(data, 0, message, headerBytes.Length, data.Length);
                }

                #endregion

                #region Send-Message

                _SendLock.Wait();
                try
                {
                    _Client.GetStream().Write(message, 0, message.Length);
                    _Client.GetStream().Flush();
                }
                finally
                {
                    _SendLock.Release();
                }

                return true;

                #endregion
            }
            catch (ObjectDisposedException ObjDispInner)
            {
                Log("*** MessageWrite server disconnected (obj disposed exception): " + ObjDispInner.Message);
                disconnectDetected = true;
                return false;
            }
            catch (SocketException SockInner)
            {
                Log("*** MessageWrite server disconnected (socket exception): " + SockInner.Message);
                disconnectDetected = true;
                return false;
            }
            catch (InvalidOperationException InvOpInner)
            {
                Log("*** MessageWrite server disconnected (invalid operation exception): " + InvOpInner.Message);
                disconnectDetected = true;
                return false;
            }
            catch (IOException IOInner)
            {
                Log("*** MessageWrite server disconnected (IO exception): " + IOInner.Message);
                disconnectDetected = true;
                return false;
            }
            catch (Exception e)
            {
                LogException("MessageWrite", e);
                disconnectDetected = true;
                return false;
            }
            finally
            {
                if (disconnectDetected)
                {
                    _Connected = false;
                    Dispose();
                }
            }
        }

        private async Task<bool> MessageWriteAsync(byte[] data)
        {
            bool disconnectDetected = false;

            try
            {
                #region Check-if-Connected

                if (_Client == null)
                {
                    Log("MessageWriteAsync client is null");
                    disconnectDetected = true;
                    return false;
                }

                #endregion

                #region Format-Message

                string header = "";
                byte[] headerBytes;
                byte[] message;

                if (data == null || data.Length < 1)
                {
                    header += "0:";
                }
                else
                {
                    header += data.Length + ":";
                }

                headerBytes = Encoding.UTF8.GetBytes(header);
                int messageLen = headerBytes.Length;
                if (data != null && data.Length > 0)
                {
                    messageLen += data.Length;
                }

                message = new byte[messageLen];
                Buffer.BlockCopy(headerBytes, 0, message, 0, headerBytes.Length);

                if (data != null && data.Length > 0)
                {
                    Buffer.BlockCopy(data, 0, message, headerBytes.Length, data.Length);
                }

                #endregion

                #region Send-Message

                await _SendLock.WaitAsync();
                try
                {
                    _Client.GetStream().Write(message, 0, message.Length);
                    _Client.GetStream().Flush();
                }
                finally
                {
                    _SendLock.Release();
                }

                return true;

                #endregion
            }
            catch (ObjectDisposedException ObjDispInner)
            {
                Log("*** MessageWriteAsync server disconnected (obj disposed exception): " + ObjDispInner.Message);
                disconnectDetected = true;
                return false;
            }
            catch (SocketException SockInner)
            {
                Log("*** MessageWriteAsync server disconnected (socket exception): " + SockInner.Message);
                disconnectDetected = true;
                return false;
            }
            catch (InvalidOperationException InvOpInner)
            {
                Log("*** MessageWriteAsync server disconnected (invalid operation exception): " + InvOpInner.Message);
                disconnectDetected = true;
                return false;
            }
            catch (IOException IOInner)
            {
                Log("*** MessageWriteAsync server disconnected (IO exception): " + IOInner.Message);
                disconnectDetected = true;
                return false;
            }
            catch (Exception e)
            {
                LogException("MessageWriteAsync", e);
                disconnectDetected = true;
                return false;
            }
            finally
            {
                if (disconnectDetected)
                {
                    _Connected = false;
                    Dispose();
                }
            }
        }

        #endregion
    }

}
