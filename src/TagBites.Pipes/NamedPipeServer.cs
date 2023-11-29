using System.IO.Pipes;
using System.Reflection;

namespace TagBites.Pipes;

[PublicAPI]
public class NamedPipeServer : IDisposable
{
    private bool _enabled;
    private Task? _waitForConnectionsTask;
    private CancellationTokenSource? _cancellationTokenSource;

    public string PipeName { get; }
    public event EventHandler<NamedPipeRequestEventArgs>? Request;

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
            using var reader = new StreamReader(pipe);
            await using var writer = new StreamWriter(pipe);

            writer.AutoFlush = true;

            while (!token.IsCancellationRequested)
            {
                // Input
                var address = await ReadLineAsync(reader).ConfigureAwait(false);
                var message = await ReadLineAsync(reader).ConfigureAwait(false);

                // Execute
                string? response = null;
                Exception? exception = null;

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

                // Response
                if (exception == null)
                {
                    await WriteLineAsync(writer, "ok").ConfigureAwait(false);
                    await WriteLineAsync(writer, response).ConfigureAwait(false);
                }
                else
                {
                    if (exception is TargetInvocationException ti)
                        exception = ti.InnerException ?? exception;

                    await WriteLineAsync(writer, "exception").ConfigureAwait(false);
                    await WriteLineAsync(writer, exception.GetType().FullName).ConfigureAwait(false);
                    await WriteLineAsync(writer, exception.Message).ConfigureAwait(false);
                    await WriteLineAsync(writer, exception.StackTrace).ConfigureAwait(false);
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

    private static async ValueTask WriteLineAsync(StreamWriter writer, string? value)
    {
        value = NamedPipeUtils.Encode(value);
        await writer.WriteLineAsync(value).ConfigureAwait(false);
    }
    private static async ValueTask<string> ReadLineAsync(StreamReader reader)
    {
        var response = await reader.ReadLineAsync().ConfigureAwait(false);
        response = NamedPipeUtils.Decode(response);
        return response;
    }
}
