namespace TagBites.Pipes.Tests;

public class ClientServerTests : PipeTestBase
{
    [Fact]
    public Task HelloTestAsync() => RequestResponseAsync("1", "2", "ok");

    [Fact]
    public void RequestEventArgsIsEventArgs() => Assert.True(typeof(EventArgs).IsAssignableFrom(typeof(NamedPipeRequestEventArgs)));

    [Theory]
    [InlineData("\\")]
    [InlineData("\\\\")]
    [InlineData("\\\\\\")]
    [InlineData("\r")]
    [InlineData("\n")]
    [InlineData("\n\r\r\n")]
    [InlineData("\\n\n")]
    [InlineData("\\r\r")]
    [InlineData("\\r\r\\n\n")]
    public Task EncodingTestAsync(string value) => RequestResponseAsync(value, value, value);


    private async Task RequestResponseAsync(string address, string message, string response)
    {
        var pipeName = CreatePipeName();
        string? receivedAddress = null;
        string? receivedMessage = null;

        using var server = CreateServer(pipeName, e =>
        {
            receivedAddress = e.Address;
            receivedMessage = e.Message;
            return response;
        });

        using var client = await ConnectAsync(pipeName);
        var received = await AssertCompletesAsync(client.SendRequestAsync(address, message));

        Assert.Equal(address, receivedAddress);
        Assert.Equal(message, receivedMessage);
        Assert.Equal(response, received);
    }
}
