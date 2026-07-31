using System.Text;

namespace TagBites.Pipes;

/// <summary>
/// Provides data for the <see cref="NamedPipeServer.Request"/> event.
/// </summary>
[PublicAPI]
public class NamedPipeRequestEventArgs : EventArgs
{
    private readonly string? _message;

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
    /// <exception cref="InvalidOperationException">
    /// <see cref="NamedPipeServer.UseMessageStream"/> is on, so the message arrives as
    /// <see cref="MessageStream"/> and is never held as a string.
    /// </exception>
    public string Message => _message
        ?? throw new InvalidOperationException($"The request arrives as {nameof(MessageStream)}, because {nameof(NamedPipeServer)}.{nameof(NamedPipeServer.UseMessageStream)} is on.");

    /// <summary>
    /// Gets the message of the request as a stream.
    /// </summary>
    /// <remarks>
    /// Valid while the handler runs.
    /// Whatever the handler leaves unread is skipped, so the next request still starts at a message boundary.
    /// </remarks>
    public Stream MessageStream { get; }

    /// <summary>
    /// Gets or sets the text sent back to the client.
    /// </summary>
    /// <remarks><c>null</c> reaches the client as an empty string.</remarks>
    public string? Response { get; set; }

    /// <summary>
    /// Gets or sets the task the server waits for before it reads the response.
    /// </summary>
    /// <remarks>
    /// Set this to handle a request asynchronously.
    /// The handler assigns the task and returns,
    /// and the server reads the response once the task completes.
    /// An exception from the task reaches the client the same way as one thrown by the handler itself.
    /// </remarks>
    public Task? ResultTask { get; set; }

    internal Func<Stream, Task>? ResponseWriter { get; private set; }

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

        _message = message;
        MessageStream = new MemoryStream(new UTF8Encoding(false, true).GetBytes(message), false);
    }
    internal NamedPipeRequestEventArgs(NamedPipeConnectionContext context, string address, Stream messageStream)
    {
        Context = context;
        Address = address;

        MessageStream = messageStream;
    }


    /// <summary>
    /// Answers with whatever the callback writes into the stream, without holding the whole response in memory.
    /// </summary>
    /// <param name="writeResponse">Writes the response body.</param>
    /// <remarks>
    /// The connection has to be on encoding version <c>3</c>, because earlier versions carry text only.
    /// A failure after the callback started writing breaks the connection, since the answer is already on its way.
    /// Read <see cref="MessageStream"/> in the handler, not in this callback:
    /// the request is consumed to the end before the answer starts, so by then it is empty.
    /// </remarks>
    public void SetResponse(Func<Stream, Task> writeResponse)
    {
        if (writeResponse == null)
            throw new ArgumentNullException(nameof(writeResponse));

        ResponseWriter = writeResponse;
    }
}
