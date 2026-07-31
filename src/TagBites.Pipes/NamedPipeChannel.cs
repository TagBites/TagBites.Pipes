namespace TagBites.Pipes;

/// <summary>
/// Carries one message at a time over a pipe, hiding how a message is framed on the wire.
/// </summary>
internal abstract class NamedPipeChannel : IDisposable
{
    /// <summary>
    /// Reads one message, or returns <c>null</c> when the other side closed the pipe.
    /// </summary>
    public abstract string? Read();
    public abstract void Write(string? value);

    /// <inheritdoc cref="Read"/>
    public abstract ValueTask<string?> ReadAsync();
    public abstract ValueTask WriteAsync(string? value);

    /// <summary>
    /// Opens the next message as a stream, or returns <c>null</c> when the other side closed the pipe.
    /// </summary>
    public abstract ValueTask<Stream?> OpenReadAsync();

    public abstract void Dispose();
}
