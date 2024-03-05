using System.Net.Sockets;
using System.Net;
using RealtimeChat.Entities.Exceptions;

namespace RealtimeChat.Entities.Server
{
    class RealtimeServer
    {
        public IPAddress ServerAddress { get; private set; }
        public string Host { get; private set; }
        public int Port { get; private set; }
        public TcpListener? Server { get; private set; }
        public List<ServerClient> Clients { get; private set; } = new List<ServerClient>();
        public RealtimeServer(string Host, int Port)
        {
            if (!System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable())
            {
                throw new ServerConnectionException("No internter connection");
            }

            if (Port < 0 || Port >= 65535)
            {
                throw new ArgumentOutOfRangeException("Port out of rande 0 - 65535");
            }

            this.Host = Host;
            this.Port = Port;

            IPHostEntry host = Dns.GetHostEntry(Dns.GetHostName());
            if (host.AddressList.Length == 0)
            {
                throw new ServerConnectionException("Error aquiring host Ip address");
            }

            foreach(var address in host.AddressList)
            {
                if (address.AddressFamily == AddressFamily.InterNetwork)
                {
                    ServerAddress = address;
                }
            }

            if(ServerAddress == null)
            {
                throw new ServerConnectionException("Could not resolve hostname");
            }

            Server = new TcpListener(ServerAddress, Port);
            Server.Start();

            LogLine($"Server started on {ServerAddress}:{Port}", ConsoleColor.Green);
        }

        public void Run()
        {
            if (Server == null)
            {
                throw new ServerConnectionException("TCP Server cannot be null, be sure you instanciated the RealtimeServer object corretly.");
            }

            while (true)
            {
                Socket client = Server.AcceptSocketAsync().Result;
                ServerClient newClient = new(client, this);
                newClient.Connect();
                Clients.Add(newClient);

                LogLine("Clients: " + Clients.Count);
            }
        }

        public void BroadcastMessage(ServerClient? originClient, string message)
        {
            LogLine($"New Broadcast message by {originClient?.Client.RemoteEndPoint}: {message}", ConsoleColor.Blue);

            foreach (var client in Clients)
            {
                if (client.Equals(originClient))
                {
                    continue;
                }

                if (!client.IsConnected())
                {
                    continue;
                }

                client.Send(message);
            }

            Clients.RemoveAll(x => !x.IsConnected());
        }

        public void DisconnectClient(ServerClient clientToDisconnect)
        {
            BroadcastMessage(clientToDisconnect, $"Client {clientToDisconnect.Client.RemoteEndPoint} has been disconnected");
            Clients.Remove(clientToDisconnect);
        }

        public static void Log(string message, ConsoleColor? foregroundColor = null, ConsoleColor? backgroundColor = null)
        {
            ConsoleColor defaultForeground = Console.ForegroundColor;
            ConsoleColor defaultBackground = Console.BackgroundColor;

            if(foregroundColor != null)
            {
                Console.ForegroundColor = foregroundColor.Value;
            }

            if (backgroundColor != null)
            {
                Console.BackgroundColor = backgroundColor.Value;
            }

            Console.Write(message);

            Console.ForegroundColor = defaultForeground;
            Console.BackgroundColor = defaultBackground;
        }

        public static void LogLine(string message, ConsoleColor? foregroundColor = null, ConsoleColor? backgroundColor = null)
        {
            Log(message + "\n", foregroundColor, backgroundColor);
        }
    }
}
