namespace TagBites.Pipes;

[PublicAPI]
public class NamedPipeClientPoolLink : IDisposable
{
    private readonly NamedPipeClientPool _pool;
    private NamedPipeClient? _client;

    public string PipeName => _pool.PipeName;
    public bool IsConnected => _client?.IsConnected ?? false;
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


    public void Connect() => Client.Connect();
    public void Connect(int timeout) => Client.Connect(timeout);

    public Task ConnectAsync() => Client.ConnectAsync();
    public Task ConnectAsync(int timeout, CancellationToken token) => Client.ConnectAsync(timeout, token);

    public string SendRequest(string address, string message) => Client.SendRequest(address, message);
    public Task<string> SendRequestAsync(string address, string message) => Client.SendRequestAsync(address, message);

    public void Dispose()
    {
        var client = _client;
        if (client == null)
            return;

        _client = null;
        _pool.ReturnConnection(client);
    }
}
