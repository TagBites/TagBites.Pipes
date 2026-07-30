namespace TagBites.Pipes.Tests;

public class ClientTests : PipeTestBase
{
    [Fact]
    public void ClientRejectsNullPipeName() => Assert.Throws<ArgumentNullException>(() => new NamedPipeClient(null!));

    [Fact]
    public async Task DisposedClientIsNotConnectedAsync()
    {
        var pipeName = CreatePipeName();
        using var server = CreateServer(pipeName);

        var client = await ConnectAsync(pipeName);
        Assert.True(client.IsConnected);

        client.Dispose();

        Assert.True(client.IsDisposed);
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task DisposedClientThrowsObjectDisposedAsync()
    {
        var pipeName = CreatePipeName();
        using var server = CreateServer(pipeName);

        var client = await ConnectAsync(pipeName);
        client.Dispose();

        Assert.Throws<ObjectDisposedException>(() => client.Connect());
        Assert.Throws<ObjectDisposedException>(() => client.SendRequest("a", "x"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.SendRequestAsync("a", "x"));
    }

    [Fact]
    public void RepeatedDisposeIsIgnored()
    {
        var client = new NamedPipeClient(CreatePipeName());

        client.Dispose();
        client.Dispose();
    }

    [Fact]
    public async Task RequestRejectsNullArgumentsAsync()
    {
        var pipeName = CreatePipeName();
        using var server = CreateServer(pipeName);
        using var client = await ConnectAsync(pipeName);

        Assert.Throws<ArgumentNullException>(() => client.SendRequest(null!, "x"));
        Assert.Throws<ArgumentNullException>(() => client.SendRequest("a", null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.SendRequestAsync(null!, "x"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.SendRequestAsync("a", null!));
    }

    [Theory]
    [InlineData("--")]
    [InlineData("--cfg-encode")]
    [InlineData("--anything")]
    public async Task RequestRejectsReservedAddressAsync(string address)
    {
        var pipeName = CreatePipeName();
        using var server = CreateServer(pipeName, e => e.Message);
        using var client = await ConnectAsync(pipeName);

        Assert.Throws<ArgumentException>(() => client.SendRequest(address, "x"));
        await Assert.ThrowsAsync<ArgumentException>(() => client.SendRequestAsync(address, "x"));

        Assert.Equal("still alive", await AssertCompletesAsync(client.SendRequestAsync("echo", "still alive")));
    }

    [Fact]
    public async Task ConnectedClientUsesAsynchronousPipeAsync()
    {
        var pipeName = CreatePipeName();
        using var server = CreateServer(pipeName, e => e.Message);

        using var client = await ConnectAsync(pipeName);
        Assert.Equal("x", await AssertCompletesAsync(client.SendRequestAsync("echo", "x")));

        var stream = typeof(NamedPipeClient)
            .GetField("_client", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(client);

        Assert.True(((System.IO.Pipes.PipeStream)stream!).IsAsync);
    }

    [Fact]
    public async Task ReconnectAfterDisposeIsRejectedAsync()
    {
        var pipeName = CreatePipeName();
        using var server = CreateServer(pipeName);

        var client = await ConnectAsync(pipeName);
        Assert.Equal("pong", await client.SendRequestAsync("a", "x"));
        client.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ConnectAsync(ConnectTimeout, CancellationToken.None));
    }
}
