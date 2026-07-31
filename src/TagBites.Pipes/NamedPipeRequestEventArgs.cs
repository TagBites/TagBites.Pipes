namespace TagBites.Pipes;

/// <summary>
/// Provides data for the <see cref="NamedPipeServer.Request"/> event.
/// </summary>
[PublicAPI]
public class NamedPipeRequestEventArgs : EventArgs
{
    /// <summary>
    /// Gets the context of the connection the request arrived on.
    /// </summary>
    public NamedPipeConnectionContext Context { get; }

    /// <summary>
    /// Gets the address of the request, which names the operation the client asks for.
    /// </summary>
    public string Address { get; }

    /// <summary>
    /// Gets the message of the request.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets or sets the text sent back to the client.
    /// </summary>
    /// <remarks><c>null</c> reaches the client as an empty string.</remarks>
    public string? Response { get; set; }

    /// <summary>
    /// Gets or sets the task the server waits for before it reads <see cref="Response"/>.
    /// </summary>
    /// <remarks>
    /// Set this to handle a request asynchronously. The handler assigns the task and returns, and
    /// the server reads <see cref="Response"/> once the task completes. An exception from the task
    /// reaches the client the same way as one thrown by the handler itself.
    /// </remarks>
    public Task? ResultTask { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="NamedPipeRequestEventArgs"/> class.
    /// </summary>
    /// <param name="context">The context of the connection the request arrived on.</param>
    /// <param name="address">The address of the request.</param>
    /// <param name="message">The message of the request.</param>
    public NamedPipeRequestEventArgs(NamedPipeConnectionContext context, string address, string message)
    {
        Context = context;
        Address = address;
        Message = message;
    }
}
