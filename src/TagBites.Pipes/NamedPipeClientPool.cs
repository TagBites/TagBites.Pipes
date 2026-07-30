using System.Collections.Concurrent;

namespace TagBites.Pipes;

[PublicAPI]
public class NamedPipeClientPool : IDisposable
{
    private readonly SemaphoreSlim _semaphore;
    private readonly int _maxConnections;
    private readonly ConcurrentBag<NamedPipeClient> _connections;

    public string PipeName { get; }
    public bool IsDisposed { get; private set; }

    public NamedPipeClientPool(string pipeName, int maxConnections)
    {
        if (pipeName == null)
            throw new ArgumentNullException(nameof(pipeName));
        if (maxConnections < 1)
            throw new ArgumentOutOfRangeException(nameof(maxConnections));

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
        ThrowIfDisposed();
        _semaphore.Wait();

        var connection = TakeConnection();
        try
        {
            connection.Connect();
            return connection;
        }
        catch
        {
            connection.Dispose();
            _semaphore.Release();
            throw;
        }
    }
    private async Task<NamedPipeClient> GetConnectionCoreAsync()
    {
        ThrowIfDisposed();
        await _semaphore.WaitAsync().ConfigureAwait(false);

        var connection = TakeConnection();
        try
        {
            await connection.ConnectAsync().ConfigureAwait(false);
            return connection;
        }
        catch
        {
            connection.Dispose();
            _semaphore.Release();
            throw;
        }
    }
    private NamedPipeClient TakeConnection()
    {
        while (_connections.TryTake(out var connection))
        {
            if (connection.IsConnected)
                return connection;

            connection.Dispose();
        }

        return new NamedPipeClient(PipeName);
    }
    internal void ReturnConnection(NamedPipeClient connection)
    {
        if (connection.IsConnected && !IsDisposed)
            _connections.Add(connection);
        else
            connection.Dispose();

        _semaphore.Release();
    }

    public void Dispose()
    {
        if (IsDisposed)
            return;

        IsDisposed = true;

        // Waits for every connection to come back
        for (var i = 0; i < _maxConnections; i++)
            _semaphore.Wait();

        while (_connections.TryTake(out var connection))
            connection.Dispose();

        _semaphore.Dispose();
    }
    private void ThrowIfDisposed()
    {
        if (IsDisposed)
            throw new ObjectDisposedException(null);
    }
}
