using System.IO.Pipes;

namespace TagBites.Pipes;

[PublicAPI]
public class NamedPipeClient : IDisposable
{
    private const int DefaultConnectTimeout = 100;

    private NamedPipeClientStream? _client;
    private StreamReader? _reader;
    private StreamWriter? _writer;

    public string PipeName { get; }
    public bool IsConnected { get; private set; }
    public bool IsDisposed { get; private set; }

    internal int EncodeVersion { get; set; }

    public NamedPipeClient(string pipeName)
    {
        if (pipeName == null)
            throw new ArgumentNullException(nameof(pipeName));

        PipeName = pipeName;
    }


    public void Connect() => Connect(DefaultConnectTimeout);
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

        _reader = new StreamReader(_client);
        _writer = new StreamWriter(_client) { AutoFlush = true };

        // Connected
        IsConnected = true;

        // Config
        _ = ProcessConfigAsync(true);
    }

    public Task ConnectAsync() => ConnectAsync(DefaultConnectTimeout, CancellationToken.None);
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

        _reader = new StreamReader(_client);
        _writer = new StreamWriter(_client) { AutoFlush = true };

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
    }

    private async Task<string> SendRequestAsync(string command, string message, bool sync)
    {
        // ReSharper disable once MethodHasAsyncOverload
        return sync
            ? SendRequestCore(command, message)
            : await SendRequestCoreAsync(command, message).ConfigureAwait(false);
    }
    public string SendRequest(string address, string message)
    {
        ValidateRequest(address, message);

        return SendRequestCore(address, message);
    }
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
            _client.WaitForPipeDrain();

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
            await _writer!.FlushAsync().ConfigureAwait(false);

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
        if (address == null)
            throw new ArgumentNullException(nameof(address));
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        if (address.StartsWith(InternalCommandNames.Prefix))
            throw new ArgumentException($"An address must not start with '{InternalCommandNames.Prefix}', the prefix is reserved for internal commands.", nameof(address));
    }

    private void WriteLine(string value)
    {
        value = NamedPipeUtils.GetEncoder(EncodeVersion)(value);
        _writer!.WriteLine(value);
    }
    private string ReadLine()
    {
        var line = _reader!.ReadLine();
        if (line == null)
            throw ConnectionLost();

        return NamedPipeUtils.GetDecoder(EncodeVersion)(line);
    }

    private async ValueTask WriteLineAsync(string value)
    {
        value = NamedPipeUtils.GetEncoder(EncodeVersion)(value);
        await _writer!.WriteLineAsync(value).ConfigureAwait(false);
    }
    private async ValueTask<string> ReadLineAsync()
    {
        var line = await _reader!.ReadLineAsync().ConfigureAwait(false);
        if (line == null)
            throw ConnectionLost();

        return NamedPipeUtils.GetDecoder(EncodeVersion)(line);
    }

    private NamedPipeConnectionLostException ConnectionLost()
    {
        IsConnected = false;
        return new NamedPipeConnectionLostException();
    }

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

        Dispose(ref _writer);
        Dispose(ref _reader);
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
