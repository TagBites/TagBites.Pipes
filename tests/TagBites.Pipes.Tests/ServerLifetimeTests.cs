namespace TagBites.Pipes.Tests;

public class ServerLifetimeTests : PipeTestBase
{
    [Fact]
    public async Task DisabledServerRefusesConnectionAsync()
    {
        var pipeName = CreatePipeName();
        using var server = CreateServer(pipeName);
        await ServeOneRequestAsync(pipeName);

        server.Enabled = false;

        await AssertCompletesWithAsync<TimeoutException>(ConnectAsync(pipeName));
    }

    [Fact]
    public async Task DisposedServerRefusesConnectionAsync()
    {
        var pipeName = CreatePipeName();
        var server = CreateServer(pipeName);
        await ServeOneRequestAsync(pipeName);

        server.Dispose();

        await AssertCompletesWithAsync<TimeoutException>(ConnectAsync(pipeName));
    }

    [Fact]
    public async Task RestartedServerAcceptsConnectionAsync()
    {
        var pipeName = CreatePipeName();
        using var server = CreateServer(pipeName);

        using (var first = await AssertCompletesAsync(ConnectAsync(pipeName)))
            Assert.Equal("pong", await AssertCompletesAsync(first.SendRequestAsync("a", "x")));

        server.Enabled = false;
        server.Enabled = true;

        using var second = await AssertCompletesAsync(ConnectAsync(pipeName));
        Assert.Equal("pong", await AssertCompletesAsync(second.SendRequestAsync("b", "x")));
    }

    [Fact]
    public async Task RepeatedlyRestartedServerAcceptsConnectionAsync()
    {
        var pipeName = CreatePipeName();
        using var server = CreateServer(pipeName);

        for (var i = 0; i < 3; i++)
        {
            server.Enabled = false;
            server.Enabled = true;

            using var client = await AssertCompletesAsync(ConnectAsync(pipeName));
            Assert.Equal("pong", await AssertCompletesAsync(client.SendRequestAsync("a", "x")));
        }
    }

    [Fact]
    public async Task DisabledServerClosesActiveConnectionAsync()
    {
        var pipeName = CreatePipeName();
        using var server = CreateServer(pipeName);
        using var client = await ConnectAsync(pipeName);

        Assert.Equal("pong", await client.SendRequestAsync("a", "x"));

        server.Enabled = false;

        await AssertCompletesWithAsync<NamedPipeConnectionLostException>(client.SendRequestAsync("b", "x"));
    }

    [Fact]
    public void DisposedServerCannotBeEnabled()
    {
        var server = CreateServer(CreatePipeName());
        server.Dispose();

        Assert.Throws<ObjectDisposedException>(() => server.Enabled = true);
    }

    [Fact]
    public void ServerRejectsNullPipeName() => Assert.Throws<ArgumentNullException>(() => new NamedPipeServer(null!));


    private static async Task ServeOneRequestAsync(string pipeName)
    {
        using var client = await AssertCompletesAsync(ConnectAsync(pipeName));
        Assert.Equal("pong", await AssertCompletesAsync(client.SendRequestAsync("probe", "x")));
    }
}
