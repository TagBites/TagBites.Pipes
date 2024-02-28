namespace TagBites.Pipes;

[PublicAPI]
public class NamedPipeClientPoolLink : IDisposable
{
    private readonly NamedPipeClientPool _pool;
    private NamedPipeClient? _client;

    public string PipeName => _pool.PipeName;
    public bool IsConnected => _client?.IsConnected ?? false;

    internal NamedPipeClientPoolLink(NamedPipeClientPool pool, NamedPipeClient client)
    {
        _pool = pool;
        _client = client;
    }


    public void Connect() => _client!.Connect();
    public void Connect(int timeout) => _client!.Connect(timeout);

    public Task ConnectAsync() => _client!.ConnectAsync();
    public Task ConnectAsync(int timeout, CancellationToken token) => _client!.ConnectAsync(timeout, token);

    public string SendRequest(string address, string message) => _client!.SendRequest(address, message);
    public Task<string> SendRequestAsync(string address, string message) => _client!.SendRequestAsync(address, message);

    public void Dispose()
    {
        if (_client != null)
            try
            {
                _pool.ReturnConnection(_client);
            }
            finally
            {
                _client = null;
            }
    }
}
