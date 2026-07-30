namespace TagBites.Pipes.Tests;

public class RemoteExceptionTests : PipeTestBase
{
    [Fact]
    public async Task ServerSendsStackTraceByDefaultAsync()
    {
        var pipeName = CreatePipeName();
        using var server = CreateThrowingServer(pipeName);
        using var client = await ConnectAsync(pipeName);

        var exception = await AssertCompletesWithAsync<NamedPipeServerRemoteException>(client.SendRequestAsync("boom", "x"));

        Assert.NotEmpty(exception.RemoteStackTrace);
    }

    [Fact]
    public async Task ServerHidesStackTraceWhenTurnedOffAsync()
    {
        var pipeName = CreatePipeName();
        using var server = CreateThrowingServer(pipeName);
        server.IncludeExceptionStackTrace = false;

        using var client = await ConnectAsync(pipeName);

        var exception = await AssertCompletesWithAsync<NamedPipeServerRemoteException>(client.SendRequestAsync("boom", "x"));

        Assert.Equal(string.Empty, exception.RemoteStackTrace);
        Assert.Equal(typeof(InvalidOperationException).FullName, exception.RemoteType);
        Assert.Equal("boom", exception.Message);
    }

    [Fact]
    public async Task ConnectionStaysUsableAfterAHiddenStackTraceAsync()
    {
        var pipeName = CreatePipeName();
        using var server = new NamedPipeServer(pipeName) { IncludeExceptionStackTrace = false };
        server.Request += (_, e) => e.Response = e.Address == "boom"
            ? throw new InvalidOperationException("boom")
            : e.Message;
        server.Enabled = true;

        using var client = await ConnectAsync(pipeName);

        await AssertCompletesWithAsync<NamedPipeServerRemoteException>(client.SendRequestAsync("boom", "x"));

        Assert.Equal("still alive", await AssertCompletesAsync(client.SendRequestAsync("echo", "still alive")));
    }


    private static NamedPipeServer CreateThrowingServer(string pipeName) =>
        CreateServer(pipeName, _ => throw new InvalidOperationException("boom"));
}
