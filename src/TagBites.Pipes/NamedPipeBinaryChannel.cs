namespace TagBites.Pipes;

/// <summary>
/// A channel that can also carry bytes that are not text, and a message whose size is not known when it starts.
/// </summary>
internal abstract class NamedPipeBinaryChannel : NamedPipeChannel
{
    /// <summary>
    /// Writes one message from whatever the callback puts into the stream.
    /// </summary>
    public abstract ValueTask WriteAsync(Func<Stream, Task> write);
}
