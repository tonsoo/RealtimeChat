namespace RealtimeChat.Entities.Exceptions
{
    class ServerConnectionException : Exception
    {
        public ServerConnectionException(string? message) : base(message) { }
    }
}
