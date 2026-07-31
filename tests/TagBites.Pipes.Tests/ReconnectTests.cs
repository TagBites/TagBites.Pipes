namespace TagBites.Pipes.Tests;

public class ReconnectTests : PipeTestBase
{
    [Fact]
    public async Task ReconnectNegotiatesTheEncodingAgainAsync()
    {
        const string payload = "path\\to\\file";

        var pipeName = CreatePipeName();
        var seen = "";

        // The default for a peer that does not negotiate differs from what a current client uses,
        // so a skipped handshake shows up as a decoding mismatch.
        using var server = new NamedPipeServer(pipeName) { SupportLegacyEncoding = true };
        server.Request += (_, e) =>
        {
            seen = e.Message;
            e.Response = e.Message;
        };
        server.Enabled = true;

        using var client = new NamedPipeClient(pipeName);
        await client.ConnectAsync(ConnectTimeout, CancellationToken.None);
        await AssertCompletesAsync(client.SendRequestAsync("echo", payload));
        Assert.Equal(payload, seen);

        server.Enabled = false;
        server.Enabled = true;

        await AssertCompletesWithAsync<NamedPipeConnectionLostException>(client.SendRequestAsync("echo", payload));

        await client.ConnectAsync(ConnectTimeout, CancellationToken.None);
        await AssertCompletesAsync(client.SendRequestAsync("echo", payload));

        Assert.Equal(payload, seen);
    }

    [Fact]
    public async Task ReconnectRestoresTheConnectionAsync()
    {
        var pipeName = CreatePipeName();
        using var server = CreateServer(pipeName);

        using var client = new NamedPipeClient(pipeName);
        await client.ConnectAsync(ConnectTimeout, CancellationToken.None);
        Assert.Equal("pong", await AssertCompletesAsync(client.SendRequestAsync("a", "x")));

        server.Enabled = false;
        server.Enabled = true;

        await AssertCompletesWithAsync<NamedPipeConnectionLostException>(client.SendRequestAsync("b", "x"));

        await client.ConnectAsync(ConnectTimeout, CancellationToken.None);

        Assert.Equal("pong", await AssertCompletesAsync(client.SendRequestAsync("c", "x")));
    }
}
