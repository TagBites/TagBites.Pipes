namespace TagBites.Pipes;

public class NamedPipeServerRemoteException : Exception
{
    public string RemoteType { get; }
    public string RemoteStackTrace { get; }

    public NamedPipeServerRemoteException(string type, string message, string stackTrace)
        : base(message)
    {
        RemoteType = type;
        RemoteStackTrace = stackTrace;
    }
}
