namespace TagBites.Pipes.Tests;

public class ClientServerTests
{
    [Fact]
    public async Task HelloTestAsync()
    {
        await RequestResponseAsync("1", "2", "ok");
    }

    [Fact]
    public async Task EncodingTestAsync()
    {
        await RequestResponseAsync("\\");
        await RequestResponseAsync("\\\\");
        await RequestResponseAsync("\\\\\\");

        await RequestResponseAsync("\r");
        await RequestResponseAsync("\n");
        await RequestResponseAsync("\n\r\r\n");

        await RequestResponseAsync("\\n\n");
        await RequestResponseAsync("\\r\r");
        await RequestResponseAsync("\\r\r\\n\n");
    }

    private Task RequestResponseAsync(string encodeTest) => RequestResponseAsync(encodeTest, encodeTest, encodeTest);
    private async Task RequestResponseAsync(string address, string message, string response)
    {
        var pipeName = Guid.NewGuid().ToString("N");
        var serverTask = StartServer(pipeName, response);
        var clientTask = StartClient(pipeName, address, message);

        var (serverReceivedAddress, serverReceivedMessage) = await serverTask;
        var clientReceived = await clientTask;

        Assert.Equal(address, serverReceivedAddress);
        Assert.Equal(message, serverReceivedMessage);
        Assert.Equal(response, clientReceived);
    }
    private static async Task<(string? Address, string? Message)> StartServer(string pipeName, string response)
    {
        var tokenSource = new TaskCompletionSource();
        string? address = null;
        string? message = null;

        var sn = new NamedPipeServer(pipeName);
        sn.Request += (_, r) =>
        {
            address = r.Address;
            message = r.Message;

            r.Response = response;
            tokenSource.SetResult();
        };

        sn.Enabled = true;
        await tokenSource.Task;
        sn.Enabled = false;

        return (address, message);
    }
    private static async Task<string> StartClient(string pipeName, string address, string message)
    {
        using var client = new NamedPipeClient(pipeName);
        await client.ConnectAsync();

        return await client.SendRequestAsync(address, message);
    }
}
