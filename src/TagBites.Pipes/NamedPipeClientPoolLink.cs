namespace TagBites.Pipes;

/// <summary>
/// Holds one connection taken from a <see cref="NamedPipeClientPool"/> for as long as the link is alive,
/// so several requests run on the same connection.
/// </summary>
/// <remarks>
/// Disposing the link returns the connection to the pool.
/// Until then the pool cannot hand that connection to anyone else,
/// so a link that is never disposed shrinks the pool by one.
/// </remarks>
[PublicAPI]
public class NamedPipeClientPoolLink : IDisposable
{
    private readonly NamedPipeClientPool _pool;
    private NamedPipeClient? _client;

    /// <summary>
    /// Gets the name of the pipe the connection points to.
    /// </summary>
    public string PipeName => _pool.PipeName;

    /// <inheritdoc cref="NamedPipeClient.IsConnected"/>
    public bool IsConnected => _client?.IsConnected ?? false;

    /// <summary>
    /// Gets a value indicating whether the connection has been returned to the pool.
    /// </summary>
    public bool IsDisposed => _client == null;

    private NamedPipeClient Client
    {
        get
        {
            if (_client == null)
                throw new ObjectDisposedException(null);

            return _client;
        }
    }

    internal NamedPipeClientPoolLink(NamedPipeClientPool pool, NamedPipeClient client)
    {
        _pool = pool;
        _client = client;
    }


    /// <inheritdoc cref="NamedPipeClient.Connect(int)"/>
    public void Connect() => Client.Connect();

    /// <inheritdoc cref="NamedPipeClient.Connect(int)"/>
    public void Connect(int timeout) => Client.Connect(timeout);

    /// <inheritdoc cref="NamedPipeClient.Connect(int)"/>
    public Task ConnectAsync() => Client.ConnectAsync();

    /// <inheritdoc cref="NamedPipeClient.ConnectAsync(int,CancellationToken)"/>
    public Task ConnectAsync(int timeout, CancellationToken token) => Client.ConnectAsync(timeout, token);

    /// <inheritdoc cref="NamedPipeClient.SendRequest"/>
    public string SendRequest(string address, string message) => Client.SendRequest(address, message);

    /// <inheritdoc cref="NamedPipeClient.SendRequest"/>
    public Task<string> SendRequestAsync(string address, string message) => Client.SendRequestAsync(address, message);

    /// <summary>
    /// Returns the connection to the pool.
    /// </summary>
    public void Dispose()
    {
        var client = _client;
        if (client == null)
            return;

        _client = null;
        _pool.ReturnConnection(client);
    }
}
