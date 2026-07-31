namespace TagBites.Pipes;

/// <summary>
/// Represents one client connection on the server side, and carries state that lives as long as that connection.
/// </summary>
/// <remarks>The server owns the lifetime of this object. Consumers do not dispose it.</remarks>
[PublicAPI]
public class NamedPipeConnectionContext : IDisposable
{
    private static int s_nextId;

    /// <summary>
    /// Occurs when the connection ends, before the context is marked as disposed.
    /// </summary>
    public event EventHandler? Disposing;

    /// <summary>
    /// Gets the number identifying the connection within the process.
    /// </summary>
    public int Id { get; } = Interlocked.Increment(ref s_nextId);

    /// <summary>
    /// Gets the store for values that live as long as the connection.
    /// </summary>
    public NamedPipeConnectionBag Bag { get; } = new();

    /// <summary>
    /// Gets a value indicating whether the connection has ended.
    /// </summary>
    public bool IsDisposed { get; private set; }


    void IDisposable.Dispose()
    {
        if (IsDisposed)
            return;

        try
        {
            Disposing?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            IsDisposed = true;
        }
    }
}
