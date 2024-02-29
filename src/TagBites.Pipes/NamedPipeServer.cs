using System.IO.Pipes;
using System.Reflection;

namespace TagBites.Pipes;

[PublicAPI]
public class NamedPipeServer : IDisposable
{
    private bool _enabled;
    private Task? _waitForConnectionsTask;
    private CancellationTokenSource? _cancellationTokenSource;

    public event EventHandler<NamedPipeRequestEventArgs>? Request;

    public string PipeName { get; }
    public bool SupportLegacyEncoding { get; set; }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled != value)
            {
                _enabled = value;

                if (_enabled)
                {
                    _cancellationTokenSource = new CancellationTokenSource();
                    _waitForConnectionsTask = _waitForConnectionsTask?.ContinueWith(t => Task.Run(WaitForConnections))
                                              ?? Task.Run(WaitForConnections);
                }
                else
                {
                    _cancellationTokenSource!.Cancel();
                }
            }
        }
    }

    public NamedPipeServer(string pipeName) => PipeName = pipeName;


    private async Task WaitForConnections()
    {
        while (Enabled)
        {
            var token = _cancellationTokenSource!.Token;

            var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Message, PipeOptions.Asynchronous);
            await pipe.WaitForConnectionAsync(token).ConfigureAwait(false);

            if (!Enabled)
                await pipe.DisposeAsync().ConfigureAwait(false);
            else
                ProcessPipe(pipe, token);
        }
    }
    private void ProcessPipe(NamedPipeServerStream pipe, CancellationToken token)
    {
        Task.Run(() => ProcessPipeTask(pipe, token), token);
    }
    private async Task ProcessPipeTask(NamedPipeServerStream pipe, CancellationToken token)
    {
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
        finally
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
        }
    }

    public void Dispose() => Enabled = false;

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
