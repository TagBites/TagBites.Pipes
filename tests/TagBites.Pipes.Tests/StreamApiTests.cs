using System.Text;

namespace TagBites.Pipes.Tests;

public class StreamApiTests : PipeTestBase
{
    [Fact]
    public async Task ExistingHandlerKeepsWorkingWhenTheClientStreamsAsync()
    {
        var pipeName = CreatePipeName();
        var seen = "";

        // A handler written before the stream API existed, reading Message as always.
        using var server = CreateServer(pipeName, e =>
        {
            seen = e.Message;
            return e.Message;
        });

        using var client = await ConnectAsync(pipeName);

        var response = await AssertCompletesAsync(client.SendRequestAsync("echo",
            stream => WriteTextAsync(stream, "streamed"),
            ReadTextAsync));

        Assert.Equal("streamed", seen);
        Assert.Equal("streamed", response);
    }

    [Theory]
    [InlineData(NamedPipeUtils.LegacyEncodeVersion)]
    [InlineData(NamedPipeUtils.TextEncodeVersion)]
    public async Task MessageStreamWorksOnATextConnectionAsync(int version)
    {
        var pipeName = CreatePipeName();
        var seen = "";

        using var server = new NamedPipeServer(pipeName)
        {
            UseMessageStream = true,
            SupportLegacyEncoding = version == NamedPipeUtils.LegacyEncodeVersion
        };
        server.Request += (_, e) => e.ResultTask = HandleAsync(e);
        server.Enabled = true;

        async Task HandleAsync(NamedPipeRequestEventArgs e)
        {
            seen = await ReadTextAsync(e.MessageStream);
            e.Response = seen;
        }

        // Setting the version up front skips the handshake, so frames are ruled out.
        using var client = new NamedPipeClient(pipeName) { EncodeVersion = version };
        await client.ConnectAsync(ConnectTimeout, CancellationToken.None);

        Assert.Equal(version, client.EncodeVersion);
        Assert.Equal("plain text", await AssertCompletesAsync(client.SendRequestAsync("echo", "plain text")));
        Assert.Equal("plain text", seen);
    }

    [Fact]
    public async Task MessageStreamCarriesAStringRequestAsync()
    {
        var pipeName = CreatePipeName();
        var seen = "";

        using var server = new NamedPipeServer(pipeName) { UseMessageStream = true };
        server.Request += (_, e) => e.ResultTask = HandleAsync(e);
        server.Enabled = true;

        async Task HandleAsync(NamedPipeRequestEventArgs e)
        {
            seen = await ReadTextAsync(e.MessageStream);
            e.Response = seen;
        }

        using var client = await ConnectAsync(pipeName);

        Assert.Equal("plain text", await AssertCompletesAsync(client.SendRequestAsync("echo", "plain text")));
        Assert.Equal("plain text", seen);
    }

    [Fact]
    public async Task MessageThrowsWhenTheServerStreamsAsync()
    {
        var pipeName = CreatePipeName();

        using var server = new NamedPipeServer(pipeName) { UseMessageStream = true };
        server.Request += (_, e) => e.Response = e.Message;
        server.Enabled = true;

        using var client = await ConnectAsync(pipeName);

        var exception = await AssertCompletesWithAsync<NamedPipeServerRemoteException>(client.SendRequestAsync("echo", "x"));

        Assert.Equal(typeof(InvalidOperationException).FullName, exception.RemoteType);
    }

    [Fact]
    public async Task StreamedRequestAndResponseRoundTripAsync()
    {
        var payload = new string('j', 3 * 1024 * 1024);

        var pipeName = CreatePipeName();
        using var server = new NamedPipeServer(pipeName) { UseMessageStream = true };
        server.Request += (_, e) => e.ResultTask = HandleAsync(e);
        server.Enabled = true;

        static async Task HandleAsync(NamedPipeRequestEventArgs e)
        {
            var received = await ReadTextAsync(e.MessageStream);
            e.SetResponse(stream => WriteTextAsync(stream, received));
        }

        using var client = await ConnectAsync(pipeName);

        var response = await AssertCompletesAsync(client.SendRequestAsync("echo",
            stream => WriteTextAsync(stream, payload),
            ReadTextAsync), 30000);

        Assert.Equal(payload.Length, response.Length);
    }

    [Fact]
    public async Task UnreadRequestDoesNotBreakTheNextOneAsync()
    {
        var pipeName = CreatePipeName();

        using var server = new NamedPipeServer(pipeName) { UseMessageStream = true };

        // Deliberately ignores the request body.
        server.Request += (_, e) => e.Response = "ignored";
        server.Enabled = true;

        using var client = await ConnectAsync(pipeName);

        for (var i = 0; i < 3; i++)
            Assert.Equal("ignored", await AssertCompletesAsync(client.SendRequestAsync("echo",
                stream => WriteTextAsync(stream, new string('x', 500_000)),
                ReadTextAsync)));
    }

    [Fact]
    public async Task BytesRoundTripAsync()
    {
        var payload = new byte[256 * 1024];
        new Random(7).NextBytes(payload);

        var pipeName = CreatePipeName();
        using var server = new NamedPipeServer(pipeName) { UseMessageStream = true };
        server.Request += (_, e) => e.ResultTask = HandleAsync(e);
        server.Enabled = true;

        static async Task HandleAsync(NamedPipeRequestEventArgs e)
        {
            using var memory = new MemoryStream();
            await e.MessageStream.CopyToAsync(memory);

            var bytes = memory.ToArray();
            e.SetResponse(stream => stream.WriteAsync(bytes, 0, bytes.Length));
        }

        using var client = await ConnectAsync(pipeName);

        var response = await AssertCompletesAsync(client.SendBytesAsync("echo", payload), 30000);

        Assert.Equal(payload, response);
    }

    [Fact]
    public async Task BytesNeedTheFramedVersionAsync()
    {
        var pipeName = CreatePipeName();
        using var server = CreateServer(pipeName);

        // Pinning the connection to text skips the handshake and rules out frames.
        using var client = new NamedPipeClient(pipeName) { EncodeVersion = NamedPipeUtils.TextEncodeVersion };
        await client.ConnectAsync(ConnectTimeout, CancellationToken.None);

        await Assert.ThrowsAsync<NotSupportedException>(() => client.SendBytesAsync("echo", [1, 2, 3]));
    }

    [Fact]
    public async Task StreamedResponseNeedsTheFramedVersionAsync()
    {
        var pipeName = CreatePipeName();

        using var server = new NamedPipeServer(pipeName);
        server.Request += (_, e) =>
        {
            if (e.Address == "stream")
                e.SetResponse(stream => WriteTextAsync(stream, "streamed"));
            else
                e.Response = e.Message;
        };
        server.Enabled = true;

        using var client = new NamedPipeClient(pipeName) { EncodeVersion = NamedPipeUtils.TextEncodeVersion };
        await client.ConnectAsync(ConnectTimeout, CancellationToken.None);

        var exception = await AssertCompletesWithAsync<NamedPipeServerRemoteException>(client.SendRequestAsync("stream", "x"));
        Assert.Equal(typeof(NotSupportedException).FullName, exception.RemoteType);

        // The failure must not leave the connection half answered.
        Assert.Equal("x", await AssertCompletesAsync(client.SendRequestAsync("echo", "x")));
    }

    [Fact]
    public async Task BytesSentToATextHandlerAreReportedAsync()
    {
        var pipeName = CreatePipeName();

        // Reads Message, so it cannot receive anything that is not text.
        using var server = CreateServer(pipeName, e => e.Message);
        using var client = await ConnectAsync(pipeName);

        var exception = await AssertCompletesWithAsync<NamedPipeServerRemoteException>(
            client.SendBytesAsync("echo", [0x41, 0xFF, 0xFE, 0x42]));

        Assert.Equal(typeof(NotSupportedException).FullName, exception.RemoteType);

        // The connection has to survive a request the handler could not take.
        Assert.Equal("still alive", await AssertCompletesAsync(client.SendRequestAsync("echo", "still alive")));
    }

    [Fact]
    public async Task StreamedRequestRejectsAReservedAddressAsync()
    {
        var pipeName = CreatePipeName();
        using var server = CreateServer(pipeName);
        using var client = await ConnectAsync(pipeName);

        await Assert.ThrowsAsync<ArgumentException>(() => client.SendRequestAsync("--cfg-encode",
            stream => WriteTextAsync(stream, "x"),
            ReadTextAsync));
    }


    private static async Task WriteTextAsync(Stream stream, string value)
    {
        var bytes = new UTF8Encoding(false, true).GetBytes(value);
        await stream.WriteAsync(bytes, 0, bytes.Length);
    }
    private static async Task<string> ReadTextAsync(Stream stream)
    {
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);

        return new UTF8Encoding(false, true).GetString(memory.GetBuffer(), 0, (int)memory.Length);
    }
}
