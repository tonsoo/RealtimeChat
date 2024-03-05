using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Text;
using System.Security.Cryptography;
using System.IO;
using static System.Net.Mime.MediaTypeNames;

namespace RealtimeChat.Entities.Server
{
    class ServerClient
    {
        public Socket Client { get; private set; }
        public RealtimeServer Server { get; private set; }
        public NetworkStream? Stream { get; private set; }
        public Thread? ClientThread { get; private set; }
        public ServerClient(Socket client, RealtimeServer server)
        {
            Client = client;
            Server = server;
        }

        public void Connect()
        {
            if(ClientThread != null) { ClientThread.Join(); }

            RealtimeServer.LogLine("Trying to connect new client", ConsoleColor.Yellow);
            ClientThread = new Thread(Run);
            ClientThread.Start();
        }

        public void Run()
        {
            RealtimeServer.LogLine($"New client: {Client.RemoteEndPoint} is connected.", ConsoleColor.Green);

            Stream = new NetworkStream(Client);

            while (true)
            {
                while (!Stream.DataAvailable) ;

                while (Client.Available < 3) ;

                byte[] bytes = new byte[Client.Available];

                Stream.Read(bytes, 0, bytes.Length);

                string data = Encoding.UTF8.GetString(bytes);

                if (Regex.IsMatch(data, "^GET"))
                {
                    const string eol = "\r\n";

                    byte[] hashBytes = Encoding.UTF8.GetBytes(new Regex("Sec-WebSocket-Key: (.*)").Match(data).Groups[1].Value.Trim() +
                            "258EAFA5-E914-47DA-95CA-C5AB0DC85B11");
                    string base64String = Convert.ToBase64String(SHA1.Create().ComputeHash(hashBytes));

                    byte[] response = Encoding.UTF8.GetBytes($"HTTP/1.1 101 Switching Protocols{eol}Connection: Upgrade{eol}Upgrade: websocket{eol}Sec-WebSocket-Accept: {base64String}{eol}{eol}");

                    Stream.Write(response, 0, response.Length);
                }
                else
                {
                    bool mask = (bytes[1] & 0b10000000) != 0; // must be true, "All messages from the client to the server have this bit set"
                    ulong msglen = bytes[1] & (ulong)0b01111111;

                    if (msglen == 126)
                    {
                        msglen = BitConverter.ToUInt16(new byte[] { bytes[3], bytes[2] }, 0);
                    }
                    else if (msglen == 127)
                    {
                        msglen = BitConverter.ToUInt64(new byte[] { bytes[9], bytes[8], bytes[7], bytes[6], bytes[5], bytes[4], bytes[3], bytes[2] }, 0);
                    }

                    if (msglen == 0)
                    {
                        RealtimeServer.LogLine("msglen == 0", ConsoleColor.Red);
                    }
                    else if (mask)
                    {
                        if (IsConnected())
                        {
                            string text = DecodeMessage(bytes);
                            RealtimeServer.LogLine($"New message from {Client.RemoteEndPoint}: {text}");

                            Server.BroadcastMessage(this, text);
                        } else
                        {
                            Server.DisconnectClient(this);
                        }
                    }
                    else
                    {
                        RealtimeServer.LogLine("mask bit not set", ConsoleColor.Red);
                        RealtimeServer.LogLine($"Info: \n\tmask= {mask}\n\tmsglen= {msglen}", ConsoleColor.Red);
                    }
                }
            }
        }

        public void Send(string data)
        {
            if (Stream == null || !Client.Connected)
            {
                return;
            }

            try
            {
                var encodedMessage = EncodeMessageToSend(data);
                Client.Send(encodedMessage);
            } catch (Exception) { }
        }

        private string DecodeMessage(byte[] bytes)
        {
            int secondByte = bytes[1];
            int dataLength = secondByte & 127;
            int indexFirstMask = 2;
            if (dataLength == 126)
            {
                indexFirstMask = 4;
            }
            else if (dataLength == 127)
            {
                indexFirstMask = 10;
            }

            var keys = bytes.Skip(indexFirstMask).Take(4);
            int indexFirstDataByte = indexFirstMask + 4;

            var decoded = new byte[bytes.Length - indexFirstDataByte];
            for (int i = indexFirstDataByte, j = 0; i < bytes.Length; i++, j++)
            {
                decoded[j] = (byte)(bytes[i] ^ keys.ElementAt(j % 4));
            }

            return Encoding.UTF8.GetString(decoded, 0, decoded.Length);
        }

        private byte[] EncodeMessageToSend(string message)
        {
            byte[] response;
            byte[] bytesRaw = Encoding.UTF8.GetBytes(message);
            byte[] frame = new byte[10];

            int indexStartRawData;
            int length = bytesRaw.Length;

            frame[0] = (byte)129;
            if (length <= 125)
            {
                frame[1] = (byte)length;
                indexStartRawData = 2;
            }
            else if (length >= 126 && length <= 65535)
            {
                frame[1] = 126;
                frame[2] = (byte)((length >> 8) & 255);
                frame[3] = (byte)(length & 255);
                indexStartRawData = 4;
            }
            else
            {
                frame[1] = 127;
                frame[2] = (byte)((length >> 56) & 255);
                frame[3] = (byte)((length >> 48) & 255);
                frame[4] = (byte)((length >> 40) & 255);
                frame[5] = (byte)((length >> 32) & 255);
                frame[6] = (byte)((length >> 24) & 255);
                frame[7] = (byte)((length >> 16) & 255);
                frame[8] = (byte)((length >> 8) & 255);
                frame[9] = (byte)(length & 255);

                indexStartRawData = 10;
            }

            response = new byte[indexStartRawData + length];

            int reponseIdx = 0;

            for (int i = 0; i < indexStartRawData; i++)
            {
                response[reponseIdx] = frame[i];
                reponseIdx++;
            }

            for (int i = 0; i < length; i++)
            {
                response[reponseIdx] = bytesRaw[i];
                reponseIdx++;
            }

            return response;
        }

        public bool Equals(ServerClient? obj = null)
        {
            if(obj == null)
            {
                return false;
            }

            return obj.Client.RemoteEndPoint == Client.RemoteEndPoint;
        }

        public bool IsConnected()
        {
            try
            {
                return !Client.Poll(1, SelectMode.SelectRead) && Client.Available == 0;
            }
            catch (SocketException e) {
                RealtimeServer.LogLine("Socket error: " + e.Message, ConsoleColor.Red);
                return false;
            }
        }
    }
}
