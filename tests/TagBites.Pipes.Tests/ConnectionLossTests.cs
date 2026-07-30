namespace TagBites.Pipes.Tests;

public class ConnectionLossTests : PipeTestBase
{
    [Fact]
    public async Task DisconnectedClientDoesNotRaiseRequestAsync()
    {
        var pipeName = CreatePipeName();
        var addresses = new List<string>();

        using var server = CreateServer(pipeName, e =>
        {
            lock (addresses)
                addresses.Add(e.Address);

            return "pong";
        });

        var client = await ConnectAsync(pipeName);
        Assert.Equal("pong", await AssertCompletesAsync(client.SendRequestAsync("real", "x")));
        client.Dispose();

        // Asserting absence needs a wait
        await Task.Delay(500);

        lock (addresses)
            Assert.Equal("real", Assert.Single(addresses));
    }

    [Fact]
    public async Task ClosedConnectionReportsConnectionLostAsync()
    {
        var pipeName = CreatePipeName();
        NamedPipeServer? server = null;

        server = CreateServer(pipeName, _ =>
        {
            server!.Enabled = false;
            return "never sent";
        });

        using (server)
        {
            using var client = await ConnectAsync(pipeName);

            await AssertCompletesWithAsync<NamedPipeConnectionLostException>(client.SendRequestAsync("a", "x"));
        }
    }

    [Fact]
    public async Task ClosedConnectionReportsConnectionLostOnSyncPathAsync()
    {
        var pipeName = CreatePipeName();
        NamedPipeServer? server = null;

        server = CreateServer(pipeName, _ =>
        {
            server!.Enabled = false;
            return "never sent";
        });

        using (server)
        {
            using var client = new NamedPipeClient(pipeName);
            await Task.Run(() => client.Connect(ConnectTimeout));

            await AssertCompletesWithAsync<NamedPipeConnectionLostException>(Task.Run(() => client.SendRequest("a", "x")));
        }
    }

    [Fact]
    public async Task EmptyMessageIsNotTreatedAsDisconnectAsync()
    {
        var pipeName = CreatePipeName();
        using var server = CreateServer(pipeName, e => e.Message);
        using var client = await ConnectAsync(pipeName);

        Assert.Equal(string.Empty, await AssertCompletesAsync(client.SendRequestAsync("echo", string.Empty)));
        Assert.Equal("still alive", await AssertCompletesAsync(client.SendRequestAsync("echo", "still alive")));
    }
}
