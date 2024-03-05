using RealtimeChat.Entities.Server;

namespace RealtimeChat
{
    class Program
    {
        static void Main()
        {
            RealtimeServer server = new RealtimeServer("192.168.15.126", 25565);

            server.Run();
        }
    }
}