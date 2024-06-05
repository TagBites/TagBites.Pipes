using System.Collections.Concurrent;

namespace TagBites.Pipes;

[PublicAPI]
public class NamedPipeClientPool : IDisposable
{
    private SemaphoreSlim _semaphore;
    private readonly int _maxConnections;
    private readonly ConcurrentBag<NamedPipeClient> _connections;

    public string PipeName { get; }

    public NamedPipeClientPool(string pipeName, int maxConnections)
    {
        PipeName = pipeName;

        _maxConnections = maxConnections;
        _semaphore = new SemaphoreSlim(maxConnections, maxConnections);
        _connections = new ConcurrentBag<NamedPipeClient>();
    }


    public string SendRequest(string address, string message)
    {
        var connection = GetConnectionCore();
        try
        {
            return connection.SendRequest(address, message);
        }
        finally
        {
            ReturnConnection(connection);
        }
    }
    public async Task<string> SendRequestAsync(string address, string message)
    {
        var connection = await GetConnectionCoreAsync().ConfigureAwait(false);
        try
        {
            return await connection.SendRequestAsync(address, message).ConfigureAwait(false);
        }
        finally
        {
            ReturnConnection(connection);
        }
    }

    public NamedPipeClientPoolLink GetConnection() => new(this, GetConnectionCore());
    public async Task<NamedPipeClientPoolLink> GetConnectionAsync() => new(this, await GetConnectionCoreAsync().ConfigureAwait(false));

    private NamedPipeClient GetConnectionCore()
    {
        _semaphore.Wait();

        if (!_connections.TryTake(out var connection))
            connection = new NamedPipeClient(PipeName);

        try
        {
            connection.Connect();
            return connection;
        }
        catch
        {
            _semaphore.Release();
            throw;
        }
    }
    private async Task<NamedPipeClient> GetConnectionCoreAsync()
    {
        await _semaphore.WaitAsync().ConfigureAwait(false);

        if (!_connections.TryTake(out var connection))
            connection = new NamedPipeClient(PipeName);

        try
        {
            await connection.ConnectAsync().ConfigureAwait(false);
            return connection;
        }
        catch
        {
            _semaphore.Release();
            throw;
        }
    }
    internal void ReturnConnection(NamedPipeClient connection)
    {
        _connections.Add(connection);
        _semaphore.Release();
    }

    public void Dispose()
    {
        while (_semaphore.CurrentCount == _maxConnections)
            _semaphore.Wait();

        foreach (var connection in _connections)
            connection.Dispose();

        try
        {
            _semaphore.Dispose();
        }
        catch
        {
            // ignored
        }
        finally
        {
            _semaphore = null!;
        }
    }
}
