using System.Collections.Concurrent;

namespace TagBites.Pipes;

/// <summary>
/// Keeps a bounded set of reusable connections to a <see cref="NamedPipeServer"/> and hands them out to callers, so several threads can send requests at once.
/// </summary>
/// <remarks>
/// The pool does not guarantee a live connection. A connection that the server dropped while it sat idle fails the next request with <see cref="NamedPipeConnectionLostException"/>; the pool then discards it, so the request after that gets a fresh one. Callers have to handle that exception.
/// A caller blocks while every connection is busy.
/// </remarks>
[PublicAPI]
public class NamedPipeClientPool : IDisposable
{
    private readonly SemaphoreSlim _semaphore;
    private readonly int _maxConnections;
    private readonly ConcurrentBag<NamedPipeClient> _connections;

    /// <summary>
    /// Gets the name of the pipe the connections point to.
    /// </summary>
    public string PipeName { get; }

    /// <summary>
    /// Gets a value indicating whether the pool has been disposed.
    /// </summary>
    public bool IsDisposed { get; private set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="NamedPipeClientPool"/> class.
    /// </summary>
    /// <param name="pipeName">The name of the pipe to connect to.</param>
    /// <param name="maxConnections">The largest number of connections held at once.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pipeName"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxConnections"/> is less than one.</exception>
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


    /// <summary>
    /// Takes a connection, sends a request on it and returns it to the pool.
    /// </summary>
    /// <param name="address">The address naming the operation. It must not start with <c>--</c>.</param>
    /// <param name="message">The message to send.</param>
    /// <returns>The response text, which is empty when the handler set no response.</returns>
    /// <exception cref="ObjectDisposedException">The pool has been disposed.</exception>
    /// <exception cref="NamedPipeConnectionLostException">The connection broke while sending or receiving.</exception>
    /// <exception cref="NamedPipeServerRemoteException">The request handler on the server failed.</exception>
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
    /// <inheritdoc cref="SendRequest"/>
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

    /// <summary>
    /// Takes a connection for several requests in a row. Disposing the returned link gives the connection back to the pool.
    /// </summary>
    /// <returns>A link holding one connection from the pool.</returns>
    /// <exception cref="ObjectDisposedException">The pool has been disposed.</exception>
    public NamedPipeClientPoolLink GetConnection() => new(this, GetConnectionCore());

    /// <inheritdoc cref="GetConnection"/>
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

    /// <summary>
    /// Waits for every request in flight to finish, then closes all connections.
    /// </summary>
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
