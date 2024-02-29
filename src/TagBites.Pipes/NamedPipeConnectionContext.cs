namespace TagBites.Pipes;

[PublicAPI]
public class NamedPipeConnectionContext : IDisposable
{
    private static int s_nextId;

    public event EventHandler? Disposing;

    public int Id { get; } = Interlocked.Increment(ref s_nextId);
    public NamedPipeConnectionBag Bag { get; } = new();
    public bool IsDisposed { get; private set; }

    internal int EncodeVersion { get; set; }


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
