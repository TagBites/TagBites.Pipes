using System.Diagnostics;

namespace TagBites.Pipes.Tests;

public class ClientPoolTests : PipeTestBase
{
    [Fact]
    public void PoolRejectsInvalidArguments()
    {
        Assert.Throws<ArgumentNullException>(() => new NamedPipeClientPool(null!, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NamedPipeClientPool("pipe", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NamedPipeClientPool("pipe", -1));
    }

    [Fact]
    public async Task DisposeWaitsForActiveRequestAsync()
    {
        const int handlerDuration = 1000;

        var pipeName = CreatePipeName();
        using var server = CreateAsyncServer(pipeName, async _ =>
        {
            await Task.Delay(handlerDuration);
            return "slow";
        });

        var pool = new NamedPipeClientPool(pipeName, 4);
        var request = pool.SendRequestAsync("a", "x");

        await Task.Delay(200);

        var stopwatch = Stopwatch.StartNew();
        await AssertCompletesAsync(Task.Run(pool.Dispose));
        stopwatch.Stop();

        Assert.Equal("slow", await AssertCompletesAsync(request));
        Assert.True(stopwatch.ElapsedMilliseconds > handlerDuration / 2, $"Dispose returned after {stopwatch.ElapsedMilliseconds} ms without waiting for the active request.");
    }

    [Fact]
    public async Task RequestAfterDisposeThrowsObjectDisposedAsync()
    {
        var pipeName = CreatePipeName();
        using var server = CreateServer(pipeName);

        var pool = new NamedPipeClientPool(pipeName, 2);
        Assert.Equal("pong", await pool.SendRequestAsync("a", "x"));

        pool.Dispose();

        Assert.True(pool.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => pool.SendRequest("b", "x"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => pool.SendRequestAsync("b", "x"));
    }

    [Fact]
    public void RepeatedDisposeIsIgnored()
    {
        var pool = new NamedPipeClientPool(CreatePipeName(), 2);

        pool.Dispose();
        pool.Dispose();
    }

    [Fact]
    public async Task PoolRecoversAfterConnectionBreaksAsync()
    {
        var pipeName = CreatePipeName();
        var server = CreateServer(pipeName);
        using var pool = new NamedPipeClientPool(pipeName, 1);

        Assert.Equal("pong", await AssertCompletesAsync(pool.SendRequestAsync("a", "x")));

        server.Dispose();
        using var restarted = CreateServer(pipeName);

        await AssertCompletesWithAsync<NamedPipeConnectionLostException>(pool.SendRequestAsync("b", "x"));

        Assert.Equal("pong", await AssertCompletesAsync(pool.SendRequestAsync("c", "x")));
    }

    [Fact]
    public async Task DisposedLinkReturnsConnectionToPoolAsync()
    {
        var pipeName = CreatePipeName();
        using var server = CreateServer(pipeName);
        using var pool = new NamedPipeClientPool(pipeName, 1);

        for (var i = 0; i < 3; i++)
        {
            using var link = await AssertCompletesAsync(pool.GetConnectionAsync());
            Assert.Equal("pong", await AssertCompletesAsync(link.SendRequestAsync("a", "x")));
        }
    }

    [Fact]
    public async Task DisposedLinkThrowsObjectDisposedAsync()
    {
        var pipeName = CreatePipeName();
        using var server = CreateServer(pipeName);
        using var pool = new NamedPipeClientPool(pipeName, 1);

        var link = await pool.GetConnectionAsync();
        Assert.Equal("pong", await link.SendRequestAsync("a", "x"));

        link.Dispose();

        Assert.True(link.IsDisposed);
        Assert.False(link.IsConnected);
        Assert.Throws<ObjectDisposedException>(() => link.SendRequest("a", "x"));
        Assert.Throws<ObjectDisposedException>(() => link.Connect());
    }
}
