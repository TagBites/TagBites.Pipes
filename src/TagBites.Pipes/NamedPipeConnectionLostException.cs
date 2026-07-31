namespace TagBites.Pipes;

/// <summary>
/// The exception that is thrown when the connection to the other side of the pipe is gone.
/// </summary>
/// <remarks>
/// A request that fails this way has an unknown outcome. The other side may have processed it before the connection broke.
/// </remarks>
[PublicAPI]
public class NamedPipeConnectionLostException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NamedPipeConnectionLostException"/> class.
    /// </summary>
    public NamedPipeConnectionLostException()
        : base("Connection lost.")
    { }
}
