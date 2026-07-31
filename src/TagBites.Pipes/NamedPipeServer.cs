using System.IO.Pipes;
using System.Reflection;
using System.Text;

namespace TagBites.Pipes;

/// <summary>
/// Accepts client connections on a named pipe and raises <see cref="Request"/> for every request.
/// </summary>
/// <remarks>
/// The server accepts nothing until <see cref="Enabled"/> is set to <c>true</c>.
/// Each connection is served on its own task, so requests from different clients run in parallel.
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
    /// The handler runs on a thread pool thread and has no synchronization context,
    /// so code that touches a user interface has to marshal itself.
    /// Set <see cref="NamedPipeRequestEventArgs.Response"/> to answer,
    /// or <see cref="NamedPipeRequestEventArgs.ResultTask"/> to answer asynchronously.
    /// An exception thrown here reaches the client as <see cref="NamedPipeServerRemoteException"/>.
    /// </remarks>
    public event EventHandler<NamedPipeRequestEventArgs>? Request;

    /// <summary>
    /// Gets the name of the pipe the server listens on.
    /// </summary>
    public string PipeName { get; }

    /// <summary>
    /// Gets or sets a value indicating whether a client that does not negotiate is served with the encoding that predates version 2.
    /// </summary>
    /// <remarks>
    /// Every released version of this library speaks version 2,
    /// so leaving this off is correct for all of them.
    /// Turn it on only for a peer built before version 2 existed.
    /// Version 1 escapes line breaks only and drops trailing whitespace,
    /// so turning it on for a peer that speaks version 2 corrupts backslashes.
    /// Default: <c>false</c>.
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
    /// The client reads it as <see cref="NamedPipeServerRemoteException.RemoteStackTrace"/>.
    /// Set to <c>false</c> when a process that must not learn about the server internals can open the pipe.
    /// The exception type and message are always sent.
    /// Default: <c>true</c>.
    /// </remarks>
    public bool IncludeExceptionStackTrace { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether a request reaches the handler as a stream rather than a string.
    /// </summary>
    /// <remarks>
    /// Turn this on to read a large request without holding it in memory, through
    /// <see cref="NamedPipeRequestEventArgs.MessageStream"/>.
    /// <see cref="NamedPipeRequestEventArgs.Message"/> then throws, because the message is never built as a string.
    /// Existing handlers keep working while this stays off.
    /// Default: <c>false</c>.
    /// </remarks>
    public bool UseMessageStream { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the server listens for connections.
    /// </summary>
    /// <remarks>
    /// The server listens as soon as this is set to <c>true</c>.
    /// Setting it to <c>false</c> stops accepting and closes every connection that is still open,
    /// so a client in the middle of a request gets <see cref="NamedPipeConnectionLostException"/>.
    /// Default: <c>false</c>.
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
            var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
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

        // A peer that does not negotiate speaks text, so version 3 is never the default.
        NamedPipeChannel channel = new NamedPipeTextChannel(pipe)
        {
            EncodeVersion = SupportLegacyEncoding ? NamedPipeUtils.LegacyEncodeVersion : NamedPipeUtils.TextEncodeVersion
        };

        try
        {
            using var context = new NamedPipeConnectionContext();
            var negotiated = false;

            while (!token.IsCancellationRequested)
            {
                // Input
                var address = await channel.ReadAsync().ConfigureAwait(false);
                if (address == null)
                    break;

                string? response = null;
                Exception? exception = null;
                Func<Stream, Task>? responseWriter = null;
                var agreed = NamedPipeUtils.UnknownEncodeVersion;

                // Internal command
                if (address.StartsWith(InternalCommandNames.Prefix))
                {
                    var message = await channel.ReadAsync().ConfigureAwait(false);
                    if (message == null)
                        break;

                    if (address == InternalCommandNames.ConfigEncodeVersion)
                        if (negotiated)
                            exception = new InvalidOperationException("The encoding is agreed once, when the connection opens.");
                        else if (int.TryParse(message, out var version))
                        {
                            agreed = Math.Max(NamedPipeUtils.LegacyEncodeVersion, Math.Min(NamedPipeUtils.CurrentEncodeVersion, version));
                            response = agreed.ToString();
                        }
                }
                // Execute
                else
                {
                    NamedPipeRequestEventArgs? e = null;

                    if (UseMessageStream)
                    {
                        var messageStream = await channel.OpenReadAsync().ConfigureAwait(false);
                        if (messageStream == null)
                            break;

                        e = new NamedPipeRequestEventArgs(context, address, messageStream);
                    }
                    else
                    {
                        string? message = null;
                        try
                        {
                            message = await channel.ReadAsync().ConfigureAwait(false);
                        }
                        catch (DecoderFallbackException)
                        {
                            // Bytes that are not text, sent to a handler that reads a string
                            exception = new NotSupportedException($"The request is not text. Turn on {nameof(UseMessageStream)} to receive it as {nameof(NamedPipeRequestEventArgs.MessageStream)}.");
                        }

                        if (exception == null)
                        {
                            if (message == null)
                                break;

                            e = new NamedPipeRequestEventArgs(context, address, message);
                        }
                    }

                    if (e != null)
                    {
                        try
                        {
                            Request?.Invoke(this, e);

                            if (e.ResultTask is { } t)
                                await t.ConfigureAwait(false);

                            response = e.Response;
                            responseWriter = e.ResponseWriter;
                        }
                        catch (Exception ex)
                        {
                            exception = ex;
                        }

                        // The handler may have read part of the message, or none of it
                        if (e.MessageStream is NamedPipeFrameReadStream unread)
                            await unread.DrainAsync().ConfigureAwait(false);

                        // Checked before the answer starts, so the failure still reaches the client
                        if (responseWriter != null && channel is not NamedPipeBinaryChannel)
                        {
                            exception = new NotSupportedException($"A streamed response needs encoding version {NamedPipeUtils.FrameEncodeVersion}. This connection agreed on an older one, which carries text only.");
                            responseWriter = null;
                        }
                    }
                }

                // Response
                if (exception == null)
                {
                    await channel.WriteAsync("ok").ConfigureAwait(false);

                    if (responseWriter != null)
                        await ((NamedPipeBinaryChannel)channel).WriteAsync(responseWriter).ConfigureAwait(false);
                    else
                        await channel.WriteAsync(response).ConfigureAwait(false);
                }
                else
                {
                    if (exception is TargetInvocationException ti)
                        exception = ti.InnerException ?? exception;

                    await channel.WriteAsync("exception").ConfigureAwait(false);
                    await channel.WriteAsync(exception.GetType().FullName).ConfigureAwait(false);
                    await channel.WriteAsync(exception.Message).ConfigureAwait(false);
                    await channel.WriteAsync(IncludeExceptionStackTrace ? exception.StackTrace : null).ConfigureAwait(false);
                }

                // Applied after the answer, which the client still reads on the previous channel
                negotiated = true;

                if (agreed == NamedPipeUtils.FrameEncodeVersion)
                {
                    var previous = channel;
                    channel = new NamedPipeFrameChannel(pipe);
                    previous.Dispose();
                }
                else if (agreed != NamedPipeUtils.UnknownEncodeVersion)
                    ((NamedPipeTextChannel)channel).EncodeVersion = agreed;
            }
        }
        catch (Exception e) when (e is ObjectDisposedException or IOException or OperationCanceledException or InvalidDataException)
        { /* ignored */ }
        finally
        {
            channel.Dispose();
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
}
