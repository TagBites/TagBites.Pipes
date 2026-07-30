using System.IO.Pipes;
using System.Reflection;

namespace TagBites.Pipes;

[PublicAPI]
public class NamedPipeServer : IDisposable
{
    private readonly object _lock = new();
    private bool _enabled;
    private CancellationTokenSource? _cancellationTokenSource;

    public event EventHandler<NamedPipeRequestEventArgs>? Request;

    public string PipeName { get; }
    public bool SupportLegacyEncoding { get; set; }
    public bool IsDisposed { get; private set; }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (value)
                ThrowIfDisposed();

            lock (_lock)
            {
                if (_enabled == value)
                    return;

                _enabled = value;

                if (value)
                {
                    _cancellationTokenSource = new CancellationTokenSource();

                    // Fire and forget. The pipe is created before the first await,
                    // so the server is listening once this setter returns.
                    _ = ListeningCoreAsync(_cancellationTokenSource.Token);
                }
                else
                {
                    var cancellationTokenSource = _cancellationTokenSource;
                    _cancellationTokenSource = null;

                    // Cancel runs the callbacks on this thread, so every pipe closes before the setter returns.
                    // Cancelling in the background would leave an instance listening with nobody to serve it.
                    cancellationTokenSource?.Cancel();
                    cancellationTokenSource?.Dispose();
                }
            }
        }
    }

    public NamedPipeServer(string pipeName)
    {
        if (pipeName == null)
            throw new ArgumentNullException(nameof(pipeName));

        PipeName = pipeName;
    }


    private async Task ListeningCoreAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Message, PipeOptions.Asynchronous);
            var connected = false;

            // A cancelled wait would leave the instance listening
            using (token.Register(pipe.Dispose))
            {
                try
                {
                    await pipe.WaitForConnectionAsync(token).ConfigureAwait(false);
                    connected = !token.IsCancellationRequested;
                }
                catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or IOException)
                { /* ignored */ }
            }

            if (!connected)
            {
                pipe.Dispose();
                break;
            }

            ProcessPipe(pipe, token);
        }
    }
    private void ProcessPipe(NamedPipeServerStream pipe, CancellationToken token)
    {
        _ = Task.Run(() => ProcessPipeAsync(pipe, token));
    }
    private async Task ProcessPipeAsync(NamedPipeServerStream pipe, CancellationToken token)
    {
        // Unblocks the pending read
        using var registration = token.Register(pipe.Dispose);

        try
        {
            using var context = new NamedPipeConnectionContext();
            context.EncodeVersion = SupportLegacyEncoding ? NamedPipeUtils.LegacyEncodeVersion : NamedPipeUtils.CurrentEncodeVersion;

            using var reader = new StreamReader(pipe);
            await using var writer = new StreamWriter(pipe);
            writer.AutoFlush = true;

            while (!token.IsCancellationRequested)
            {
                // Input
                var address = await ReadLineAsync(context, reader).ConfigureAwait(false);
                var message = await ReadLineAsync(context, reader).ConfigureAwait(false);

                string? response = null;
                Exception? exception = null;

                // Internal command
                if (address.StartsWith("--"))
                {
                    if (address == InternalCommandNames.ConfigEncodeVersion)
                        if (int.TryParse(message, out var version))
                        {
                            context.EncodeVersion = Math.Max(NamedPipeUtils.LegacyEncodeVersion, Math.Min(NamedPipeUtils.CurrentEncodeVersion, version));
                            response = context.EncodeVersion.ToString();
                        }
                }
                // Execute
                else
                {
                    try
                    {
                        var e = new NamedPipeRequestEventArgs(context, address, message);
                        Request?.Invoke(this, e);

                        if (e.ResultTask is { } t)
                            await t.ConfigureAwait(false);

                        response = e.Response;
                    }
                    catch (Exception ex)
                    {
                        exception = ex;
                    }
                }

                // Response
                if (exception == null)
                {
                    await WriteLineAsync(context, writer, "ok").ConfigureAwait(false);
                    await WriteLineAsync(context, writer, response).ConfigureAwait(false);
                }
                else
                {
                    if (exception is TargetInvocationException ti)
                        exception = ti.InnerException ?? exception;

                    await WriteLineAsync(context, writer, "exception").ConfigureAwait(false);
                    await WriteLineAsync(context, writer, exception.GetType().FullName).ConfigureAwait(false);
                    await WriteLineAsync(context, writer, exception.Message).ConfigureAwait(false);
                    await WriteLineAsync(context, writer, exception.StackTrace).ConfigureAwait(false);
                }

                pipe.WaitForPipeDrain();
            }
        }
        catch (Exception e) when (e is ObjectDisposedException or IOException or OperationCanceledException)
        { /* ignored */ }
        finally
        {
            pipe.Dispose();
        }
    }

    public void Dispose()
    {
        if (IsDisposed)
            return;

        IsDisposed = true;
        Enabled = false;
    }
    private void ThrowIfDisposed()
    {
        if (IsDisposed)
            throw new ObjectDisposedException(null);
    }

    private async ValueTask WriteLineAsync(NamedPipeConnectionContext context, StreamWriter writer, string? value)
    {
        value = NamedPipeUtils.GetEncoder(context.EncodeVersion)(value);
        await writer.WriteLineAsync(value).ConfigureAwait(false);
    }
    private async ValueTask<string> ReadLineAsync(NamedPipeConnectionContext context, StreamReader reader)
    {
        var response = await reader.ReadLineAsync().ConfigureAwait(false);
        response = NamedPipeUtils.GetDecoder(context.EncodeVersion)(response);
        return response;
    }
}
