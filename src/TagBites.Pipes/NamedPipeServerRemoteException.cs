namespace TagBites.Pipes;

/// <summary>
/// The exception that is thrown when the request handler on the server side fails.
/// </summary>
/// <remarks>
/// The original exception cannot cross the pipe, so its type name, message and stack trace are carried as text. The server decides whether to send the stack trace, see
/// <see cref="NamedPipeServer.IncludeExceptionStackTrace"/>.
/// </remarks>
[PublicAPI]
public class NamedPipeServerRemoteException : Exception
{
    /// <summary>
    /// Gets the full name of the exception type raised on the server.
    /// </summary>
    public string RemoteType { get; }

    /// <summary>
    /// Gets the stack trace of the exception raised on the server, or an empty string when the server does not send it.
    /// </summary>
    public string RemoteStackTrace { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="NamedPipeServerRemoteException"/> class.
    /// </summary>
    /// <param name="type">The full name of the exception type raised on the server.</param>
    /// <param name="message">The message of the exception raised on the server.</param>
    /// <param name="stackTrace">The stack trace of the exception raised on the server.</param>
    public NamedPipeServerRemoteException(string type, string message, string stackTrace)
        : base(message)
    {
        RemoteType = type;
        RemoteStackTrace = stackTrace;
    }
}
