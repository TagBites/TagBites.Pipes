using System.IO.Pipes;

namespace TagBites.Pipes;

/// <summary>
/// Sends requests to a <see cref="NamedPipeServer"/> on the local machine over a single connection.
/// </summary>
/// <remarks>
/// One instance serves one request at a time and is not safe for concurrent use. Use
/// <see cref="NamedPipeClientPool"/> to send requests from several threads.
/// </remarks>
[PublicAPI]
public class NamedPipeClient : IDisposable
{
    private const int DefaultConnectTimeout = 100;

    private NamedPipeClientStream? _client;
    private NamedPipeChannel? _channel;
    private int _encodeVersion;

    /// <summary>
    /// Gets the name of the pipe the client connects to.
    /// </summary>
    public string PipeName { get; }

    /// <summary>
    /// Gets a value indicating whether the connection is believed to be alive.
    /// </summary>
    /// <remarks>
    /// This turns to <c>false</c> once a request fails or the client is disposed. A connection that the other side dropped while idle still reads as <c>true</c>, because a named pipe reveals that only when it is used.
    /// </remarks>
    public bool IsConnected { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the client has been disposed.
    /// </summary>
    public bool IsDisposed { get; private set; }

    internal int EncodeVersion
    {
        get => _encodeVersion;
        set
        {
            _encodeVersion = value;

            // The version can be set before the connection exists, so the channel is kept in step.
            if (_channel is NamedPipeTextChannel text)
                text.EncodeVersion = value;
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NamedPipeClient"/> class.
    /// </summary>
    /// <param name="pipeName">The name of the pipe to connect to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pipeName"/> is <c>null</c>.</exception>
    public NamedPipeClient(string pipeName)
    {
        if (pipeName == null)
            throw new ArgumentNullException(nameof(pipeName));

        PipeName = pipeName;
    }


    /// <inheritdoc cref="Connect(int)"/>
    public void Connect() => Connect(DefaultConnectTimeout);

    /// <summary>
    /// Opens the connection and agrees an encoding with the server. Returns at once when the client is already connected.
    /// </summary>
    /// <param name="timeout">How long to wait for the server, in milliseconds. Default: <c>100</c>.</param>
    /// <exception cref="TimeoutException">The server did not accept within the timeout.</exception>
    /// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
    public void Connect(int timeout)
    {
        ThrowIfDisposed();

        if (_client != null)
        {
            if (IsConnected)
                return;

            CloseCore();
        }

        _client = CreateStream();
        _client.Connect(timeout);

        _channel = new NamedPipeTextChannel(_client) { EncodeVersion = _encodeVersion };

        // Connected
        IsConnected = true;

        // Config
        _ = ProcessConfigAsync(true);
    }

    /// <inheritdoc cref="ConnectAsync(int,CancellationToken)"/>
    public Task ConnectAsync() => ConnectAsync(DefaultConnectTimeout, CancellationToken.None);

    /// <inheritdoc cref="Connect(int)"/>
    /// <param name="timeout">How long to wait for the server, in milliseconds. Default: <c>100</c>.</param>
    /// <param name="token">The token that cancels waiting for the server.</param>
    public async Task ConnectAsync(int timeout, CancellationToken token)
    {
        ThrowIfDisposed();

        if (_client != null)
        {
            if (IsConnected)
                return;

            CloseCore();
        }

        _client = CreateStream();
        await _client.ConnectAsync(timeout, token).ConfigureAwait(false);

        _channel = new NamedPipeTextChannel(_client) { EncodeVersion = _encodeVersion };

        // Connected
        IsConnected = true;

        // Config
        await ProcessConfigAsync(false).ConfigureAwait(false);
    }

    private NamedPipeClientStream CreateStream() => new(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

    private async Task ProcessConfigAsync(bool sync)
    {
        // Config
        if (EncodeVersion == NamedPipeUtils.UnknownEncodeVersion)
            try
            {
                var response = await SendRequestAsync(InternalCommandNames.ConfigEncodeVersion, NamedPipeUtils.CurrentEncodeVersion.ToString(), sync).ConfigureAwait(false);
                if (int.TryParse(response, out var encodeVersion))
                    EncodeVersion = encodeVersion;
            }
            catch
            {
                EncodeVersion = NamedPipeUtils.LegacyEncodeVersion;
            }

        // The server switches right after it answered, so both sides change at the same point.
        if (EncodeVersion == NamedPipeUtils.FrameEncodeVersion)
            UpgradeToFrames();
    }
    private void UpgradeToFrames()
    {
        var previous = _channel;
        _channel = new NamedPipeFrameChannel(_client!);
        previous?.Dispose();
    }

    private async Task<string> SendRequestAsync(string command, string message, bool sync)
    {
        // ReSharper disable once MethodHasAsyncOverload
        return sync
            ? SendRequestCore(command, message)
            : await SendRequestCoreAsync(command, message).ConfigureAwait(false);
    }
    /// <summary>
    /// Sends a request and waits for the response.
    /// </summary>
    /// <param name="address">The address naming the operation. It must not start with <c>--</c>.</param>
    /// <param name="message">The message to send.</param>
    /// <returns>The response text, which is empty when the handler set no response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="address"/> or <paramref name="message"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="address"/> starts with <c>--</c>, which is reserved.</exception>
    /// <exception cref="InvalidOperationException">The client is not connected.</exception>
    /// <exception cref="NamedPipeConnectionLostException">The connection broke while sending or receiving.</exception>
    /// <exception cref="NamedPipeServerRemoteException">The request handler on the server failed.</exception>
    public string SendRequest(string address, string message)
    {
        ValidateRequest(address, message);

        return SendRequestCore(address, message);
    }

    /// <summary>
    /// Sends a request written by a callback and reads the response with another one, so neither side has to hold the whole payload in memory.
    /// </summary>
    /// <param name="address">The address naming the operation. It must not start with <c>--</c>.</param>
    /// <param name="writeMessage">Writes the request body.</param>
    /// <param name="readResponse">Reads the response body.</param>
    /// <returns>Whatever <paramref name="readResponse"/> returns.</returns>
    /// <exception cref="NotSupportedException">
    /// The connection agreed on an encoding older than version <c>3</c>, which carries text only.
    /// </exception>
    /// <inheritdoc cref="SendRequest" path="/exception[@cref!='System.NotSupportedException']"/>
    public async Task<T> SendRequestAsync<T>(string address, Func<Stream, Task> writeMessage, Func<Stream, Task<T>> readResponse)
    {
        if (writeMessage == null)
            throw new ArgumentNullException(nameof(writeMessage));
        if (readResponse == null)
            throw new ArgumentNullException(nameof(readResponse));

        ValidateAddress(address);
        ThrowIfDisposed();

        var channel = RequireBinaryChannel();

        try
        {
            await channel.WriteAsync(address).ConfigureAwait(false);
            await channel.WriteAsync(writeMessage).ConfigureAwait(false);

            var responseType = await ReadLineAsync().ConfigureAwait(false);
            switch (responseType)
            {
                case "ok":
                    {
                        var stream = await channel.OpenReadAsync().ConfigureAwait(false);
                        if (stream == null)
                            throw ConnectionLost();

                        var result = await readResponse(stream).ConfigureAwait(false);

                        // The rest has to go, or the next request starts inside this response.
                        if (stream is NamedPipeFrameReadStream unread)
                            await unread.DrainAsync().ConfigureAwait(false);

                        return result;
                    }

                case "exception":
                    throw await ReadRemoteExceptionAsync().ConfigureAwait(false);

                default:
                    throw new NotSupportedException($"Unknown server response '{responseType}'.");
            }
        }
        catch (IOException)
        {
            throw ConnectionLost();
        }
    }

    /// <summary>
    /// Sends the given bytes and returns the bytes that come back.
    /// </summary>
    /// <remarks>
    /// A separate name rather than an overload of <see cref="SendRequestAsync(string,string)"/>,
    /// so that a call passing a plain <c>null</c> keeps compiling.
    /// </remarks>
    /// <inheritdoc cref="SendRequestAsync{T}" path="/param|/exception"/>
    public Task<byte[]> SendBytesAsync(string address, byte[] message)
    {
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        return SendRequestAsync(address,
            stream => stream.WriteAsync(message, 0, message.Length),
            async stream =>
            {
                using var memory = new MemoryStream();
                await stream.CopyToAsync(memory).ConfigureAwait(false);
                return memory.ToArray();
            });
    }

    /// <inheritdoc cref="SendRequest"/>
    public async Task<string> SendRequestAsync(string address, string message)
    {
        ValidateRequest(address, message);

        return await SendRequestCoreAsync(address, message).ConfigureAwait(false);
    }

    private string SendRequestCore(string address, string message)
    {
        ThrowIfDisposed();

        if (_client == null)
            throw new InvalidOperationException("The client is not connected.");

        try
        {
            // Input
            WriteLine(address);
            WriteLine(message);

            // Response
            var responseType = ReadLine();
            switch (responseType)
            {
                case "ok":
                    return ReadLine();

                case "exception":
                    {
                        var type = ReadLine();
                        var msg = ReadLine();
                        var stackTrace = ReadLine();

                        throw new NamedPipeServerRemoteException(type, msg, stackTrace);
                    }

                default:
                    throw new NotSupportedException($"Unknown server response '{responseType}'.");
            }
        }
        catch (IOException)
        {
            throw ConnectionLost();
        }
    }
    private async Task<string> SendRequestCoreAsync(string address, string message)
    {
        ThrowIfDisposed();

        if (_client == null)
            throw new InvalidOperationException("The client is not connected.");

        try
        {
            // Input
            await WriteLineAsync(address).ConfigureAwait(false);
            await WriteLineAsync(message).ConfigureAwait(false);

            // Response
            var responseType = await ReadLineAsync().ConfigureAwait(false);
            switch (responseType)
            {
                case "ok":
                    return await ReadLineAsync().ConfigureAwait(false);

                case "exception":
                    {
                        var type = await ReadLineAsync().ConfigureAwait(false);
                        var msg = await ReadLineAsync().ConfigureAwait(false);
                        var stackTrace = await ReadLineAsync().ConfigureAwait(false);

                        throw new NamedPipeServerRemoteException(type, msg, stackTrace);
                    }

                default:
                    throw new NotSupportedException($"Unknown server response '{responseType}'.");
            }
        }
        catch (IOException)
        {
            throw ConnectionLost();
        }
    }
    private static void ValidateRequest(string address, string message)
    {
        ValidateAddress(address);

        if (message == null)
            throw new ArgumentNullException(nameof(message));
    }
    private static void ValidateAddress(string address)
    {
        if (address == null)
            throw new ArgumentNullException(nameof(address));

        if (address.StartsWith(InternalCommandNames.Prefix))
            throw new ArgumentException($"An address must not start with '{InternalCommandNames.Prefix}', the prefix is reserved for internal commands.", nameof(address));
    }
    private NamedPipeBinaryChannel RequireBinaryChannel()
    {
        if (_client == null)
            throw new InvalidOperationException("The client is not connected.");
        if (_channel is not NamedPipeBinaryChannel binary)
            throw new NotSupportedException($"The connection agreed on encoding version {EncodeVersion}, which carries text only. Version {NamedPipeUtils.FrameEncodeVersion} is needed to send bytes.");

        return binary;
    }
    private async Task<NamedPipeServerRemoteException> ReadRemoteExceptionAsync()
    {
        var type = await ReadLineAsync().ConfigureAwait(false);
        var message = await ReadLineAsync().ConfigureAwait(false);
        var stackTrace = await ReadLineAsync().ConfigureAwait(false);

        return new NamedPipeServerRemoteException(type, message, stackTrace);
    }

    private void WriteLine(string value) => _channel!.Write(value);
    private string ReadLine()
    {
        var value = _channel!.Read();
        if (value == null)
            throw ConnectionLost();

        return value;
    }

    private async ValueTask WriteLineAsync(string value) => await _channel!.WriteAsync(value).ConfigureAwait(false);
    private async ValueTask<string> ReadLineAsync()
    {
        var value = await _channel!.ReadAsync().ConfigureAwait(false);
        if (value == null)
            throw ConnectionLost();

        return value;
    }

    private NamedPipeConnectionLostException ConnectionLost()
    {
        IsConnected = false;
        return new NamedPipeConnectionLostException();
    }

    /// <summary>
    /// Closes the connection. The instance cannot be connected again.
    /// </summary>
    public void Dispose()
    {
        if (IsDisposed)
            return;

        IsDisposed = true;
        CloseCore();
    }
    private void CloseCore()
    {
        IsConnected = false;

        // The version belongs to the connection, so the next one negotiates from scratch.
        _encodeVersion = NamedPipeUtils.UnknownEncodeVersion;

        Dispose(ref _channel);
        Dispose(ref _client);
    }
    private void ThrowIfDisposed()
    {
        if (IsDisposed)
            throw new ObjectDisposedException(null);
    }

    private static void Dispose<T>(ref T? disposable) where T : class, IDisposable
    {
        if (disposable != null)
            try
            {
                disposable.Dispose();
            }
            catch
            {
                // ignored
            }
            finally
            {
                disposable = null;
            }
    }
}
