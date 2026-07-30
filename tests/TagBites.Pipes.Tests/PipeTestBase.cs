namespace TagBites.Pipes.Tests;

public abstract class PipeTestBase
{
    protected const int ConnectTimeout = 2000;
    protected const int OperationTimeout = 5000;


    protected static string CreatePipeName() => Guid.NewGuid().ToString("N");

    protected static NamedPipeServer CreateServer(string pipeName) => CreateServer(pipeName, _ => "pong");
    protected static NamedPipeServer CreateServer(string pipeName, Func<NamedPipeRequestEventArgs, string?> handler)
    {
        var server = new NamedPipeServer(pipeName);
        server.Request += (_, e) => e.Response = handler(e);
        server.Enabled = true;
        return server;
    }

    protected static NamedPipeServer CreateAsyncServer(string pipeName, Func<NamedPipeRequestEventArgs, Task<string?>> handler)
    {
        var server = new NamedPipeServer(pipeName);
        server.Request += (_, e) => e.ResultTask = SetResponseAsync(e, handler);
        server.Enabled = true;
        return server;
    }
    private static async Task SetResponseAsync(NamedPipeRequestEventArgs e, Func<NamedPipeRequestEventArgs, Task<string?>> handler) => e.Response = await handler(e);

    protected static async Task<NamedPipeClient> ConnectAsync(string pipeName)
    {
        var client = new NamedPipeClient(pipeName);
        await client.ConnectAsync(ConnectTimeout, CancellationToken.None);
        return client;
    }

    protected static async Task AssertCompletesAsync(Task task, int timeout = OperationTimeout)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeout));
        Assert.True(completed == task, $"Operation did not complete within {timeout} ms.");

        await task;
    }
    protected static async Task<T> AssertCompletesAsync<T>(Task<T> task, int timeout = OperationTimeout)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeout));
        Assert.True(completed == task, $"Operation did not complete within {timeout} ms.");

        return await task;
    }

    protected static async Task<TException> AssertCompletesWithAsync<TException>(Task task, int timeout = OperationTimeout)
        where TException : Exception
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeout));
        Assert.True(completed == task, $"Operation did not complete within {timeout} ms.");

        return await Assert.ThrowsAsync<TException>(() => task);
    }
}
