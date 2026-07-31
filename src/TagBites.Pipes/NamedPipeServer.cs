using System.IO.Pipes;
using System.Reflection;

namespace TagBites.Pipes;

/// <summary>
/// Accepts client connections on a named pipe and raises <see cref="Request"/> for every request.
/// </summary>
/// <remarks>
/// The server accepts nothing until <see cref="Enabled"/> is set to <c>true</c>. Each connection is
/// served on its own task, so requests from different clients run in parallel.
/// </remarks>
[PublicAPI]
public class NamedPipeServer : IDisposable
{
    private readonly object _lock = new();
    private bool _enabled;
    private CancellationTokenSource? _cancellationTokenSource;

    /// <summary>
    /// Occurs when a client sends a request.
    /// </summary>
    /// <remarks>
    /// The handler runs on a thread pool thread and has no synchronization context, so code that
    /// touches a user interface has to marshal itself. Set
    /// <see cref="NamedPipeRequestEventArgs.Response"/> to answer, or
    /// <see cref="NamedPipeRequestEventArgs.ResultTask"/> to answer asynchronously. An exception
    /// thrown here reaches the client as <see cref="NamedPipeServerRemoteException"/>.
    /// </remarks>
    public event EventHandler<NamedPipeRequestEventArgs>? Request;

    /// <summary>
    /// Gets the name of the pipe the server listens on.
    /// </summary>
    public string PipeName { get; }

    /// <summary>
    /// Gets or sets a value indicating whether a client that does not negotiate an encoding is
    /// served with the encoding that predates version 2.
    /// </summary>
    /// <remarks>
    /// Every released version of this library speaks version 2, so leaving this off is correct for
    /// all of them. Turn it on only for a peer built before version 2 existed. Version 1 escapes
    /// line breaks only and drops trailing whitespace, so turning it on for a peer that speaks
    /// version 2 corrupts backslashes. Default: <c>false</c>.
    /// </remarks>
    public bool SupportLegacyEncoding { get; set; }

    /// <summary>
    /// Gets a value indicating whether the server has been disposed.
    /// </summary>
    public bool IsDisposed { get; private set; }

    /// <summary>
    /// Gets or sets a value indicating whether the stack trace of a failed request is sent to the client.
    /// </summary>
    /// <remarks>
    /// The client reads it as <see cref="NamedPipeServerRemoteException.RemoteStackTrace"/>. Set to
    /// <c>false</c> when a process that must not learn about the server internals can open the pipe.
    /// The exception type and message are always sent. Default: <c>true</c>.
    /// </remarks>
    public bool IncludeExceptionStackTrace { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the server listens for connections.
    /// </summary>
    /// <remarks>
    /// The server listens as soon as this is set to <c>true</c>. Setting it to <c>false</c> stops
    /// accepting and closes every connection that is still open, so a client in the middle of a
    /// request gets <see cref="NamedPipeConnectionLostException"/>. Default: <c>false</c>.
    /// </remarks>
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

    /// <summary>
    /// Initializes a new instance of the <see cref="NamedPipeServer"/> class.
    /// </summary>
    /// <param name="pipeName">The name of the pipe to listen on.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pipeName"/> is <c>null</c>.</exception>
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
                var message = address != null
                    ? await ReadLineAsync(context, reader).ConfigureAwait(false)
                    : null;

                // End of stream
                if (address == null || message == null)
                    break;

                string? response = null;
                Exception? exception = null;

                // Internal command
                if (address.StartsWith(InternalCommandNames.Prefix))
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
                    await WriteLineAsync(context, writer, IncludeExceptionStackTrace ? exception.StackTrace : null).ConfigureAwait(false);
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

    /// <summary>
    /// Stops the server and closes every open connection. The instance cannot be enabled again.
    /// </summary>
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
    private async ValueTask<string?> ReadLineAsync(NamedPipeConnectionContext context, StreamReader reader)
    {
        var line = await reader.ReadLineAsync().ConfigureAwait(false);
        return line != null
            ? NamedPipeUtils.GetDecoder(context.EncodeVersion)(line)
            : null;
    }
}
