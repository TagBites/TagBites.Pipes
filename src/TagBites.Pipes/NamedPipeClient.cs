using System.IO.Pipes;

namespace TagBites.Pipes;

[PublicAPI]
public class NamedPipeClient : IDisposable
{
    private NamedPipeClientStream? _client;
    private StreamReader? _reader;
    private StreamWriter? _writer;

    public string PipeName { get; }
    public bool IsConnected { get; private set; }

    internal int EncodeVersion { get; set; }

    public NamedPipeClient(string pipeName) => PipeName = pipeName;


    public void Connect() => Connect(100);
    public void Connect(int timeout)
    {
        if (_client != null)
        {
            if (IsConnected)
                return;

            Dispose();
        }

        _client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        _client.Connect(timeout);

        _reader = new StreamReader(_client);
        _writer = new StreamWriter(_client) { AutoFlush = true };

        // Config
        _ = ProcessConfigAsync(true);

        // Connected
        IsConnected = true;
    }

    public Task ConnectAsync() => ConnectAsync(100, CancellationToken.None);
    public async Task ConnectAsync(int timeout, CancellationToken token)
    {
        if (_client != null)
        {
            if (IsConnected)
                return;

            Dispose();
        }

        _client = new NamedPipeClientStream(PipeName);
        await _client.ConnectAsync(timeout, token).ConfigureAwait(false);

        _reader = new StreamReader(_client);
        _writer = new StreamWriter(_client) { AutoFlush = true };

        // Config
        await ProcessConfigAsync(true).ConfigureAwait(false);

        // Connected
        IsConnected = true;
    }

    private async Task ProcessConfigAsync(bool sync)
    {
        // Config
        if (EncodeVersion == 0)
            try
            {
                var response = await SendRequestAsync(InternalCommandNames.ConfigEncodeVersion, NamedPipeUtils.CurrentEncodeVersion.ToString(), sync);
                if (int.TryParse(response, out var encodeVersion))
                    EncodeVersion = encodeVersion;
            }
            catch
            {
                EncodeVersion = 1;
            }
    }

    private async Task<string> SendRequestAsync(string command, string message, bool sync)
    {
        // ReSharper disable once MethodHasAsyncOverload
        return sync
            ? SendRequest(command, message)
            : await SendRequestAsync(command, message).ConfigureAwait(false);
    }
    public string SendRequest(string address, string message)
    {
        if (_client == null)
            throw new InvalidOperationException();

        if (address == null)
            throw new ArgumentNullException(nameof(address));
        if (message == null)
            throw new ArgumentNullException(nameof(message));

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
            IsConnected = false;
            throw new NamedPipeConnectionLostException();
        }
    }
    public async Task<string> SendRequestAsync(string address, string message)
    {
        if (_client == null)
            throw new InvalidOperationException();

        if (address == null)
            throw new ArgumentNullException(nameof(address));
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        try
        {
            // Input
            await WriteLineAsync(address).ConfigureAwait(false);
            await WriteLineAsync(message).ConfigureAwait(false);
            await _writer!.FlushAsync().ConfigureAwait(false);

            // Response
            var responseType = await ReadLineAsync();
            switch (responseType)
            {
                case "ok":
                    return await ReadLineAsync();

                case "exception":
                    {
                        var type = await ReadLineAsync();
                        var msg = await ReadLineAsync();
                        var stackTrace = await ReadLineAsync();

                        throw new NamedPipeServerRemoteException(type, msg, stackTrace);
                    }

                default:
                    throw new NotSupportedException($"Unknown server response '{responseType}'.");
            }
        }
        catch (IOException)
        {
            IsConnected = false;
            throw new NamedPipeConnectionLostException();
        }
    }

    private void WriteLine(string value)
    {
        value = NamedPipeUtils.GetEncoder(EncodeVersion)(value);
        _writer!.WriteLine(value);
    }
    private string ReadLine()
    {
        if (_reader!.Peek() == 0)
            return string.Empty;

        var response = _reader.ReadLine();
        response = NamedPipeUtils.GetDecoder(EncodeVersion)(response);
        return response;
    }

    private async ValueTask WriteLineAsync(string value)
    {
        value = NamedPipeUtils.GetEncoder(EncodeVersion)(value);
        await _writer!.WriteLineAsync(value).ConfigureAwait(false);
    }
    private async ValueTask<string> ReadLineAsync()
    {
        var response = await _reader!.ReadLineAsync().ConfigureAwait(false);
        response = NamedPipeUtils.GetDecoder(EncodeVersion)(response);
        return response;
    }

    public void Dispose()
    {
        Dispose(ref _writer);
        Dispose(ref _reader);
        Dispose(ref _client);
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
