using System.IO.Pipes;
using System.Text;

namespace TagBites.Pipes.Tests;

/// <summary>
/// Version 3 replaces the escaped line with a length prefix and the raw UTF-8 bytes.
/// The format is a wire contract, so these tests pin the exact bytes.
/// </summary>
public class FrameProtocolTests : PipeTestBase
{
    [Fact]
    public async Task ConnectionNegotiatesFramesAsync()
    {
        var pipeName = CreatePipeName();
        using var server = CreateServer(pipeName, e => e.Message);

        using var client = await ConnectAsync(pipeName);

        Assert.Equal(NamedPipeUtils.FrameEncodeVersion, client.EncodeVersion);
    }

    [Theory]
    [InlineData("quote'and\"quote")]
    [InlineData("back\\slash")]
    [InlineData("\ttabbed\t")]
    [InlineData("trailing spaces   ")]
    [InlineData("line\nbreak")]
    [InlineData("carriage\rreturn")]
    [InlineData("null\0char")]
    [InlineData("")]
    public async Task FramesCarryEveryCharacterUnchangedAsync(string payload)
    {
        var pipeName = CreatePipeName();
        var seen = "";

        using var server = CreateServer(pipeName, e =>
        {
            seen = e.Message;
            return e.Message;
        });

        using var client = await ConnectAsync(pipeName);

        Assert.Equal(payload, await AssertCompletesAsync(client.SendRequestAsync("echo", payload)));
        Assert.Equal(payload, seen);
    }

    [Fact]
    public async Task FramesCarryALargePayloadAsync()
    {
        var payload = new string('x', 4 * 1024 * 1024);

        var pipeName = CreatePipeName();
        using var server = CreateServer(pipeName, e => e.Message);
        using var client = await ConnectAsync(pipeName);

        var response = await AssertCompletesAsync(client.SendRequestAsync("echo", payload), 30000);

        Assert.Equal(payload.Length, response.Length);
    }

    [Fact]
    public async Task RawPeerReadsTheFrameFormatAsync()
    {
        var pipeName = CreatePipeName();
        using var server = CreateServer(pipeName, e => e.Message);

        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(ConnectTimeout);

        // The handshake still travels as text, because the peer may be older.
        var writer = new StreamWriter(pipe, new UTF8Encoding(false, true), 1024, true) { AutoFlush = true };
        var reader = new StreamReader(pipe, Encoding.UTF8, true, 1024, true);

        writer.WriteLine("--cfg-encode");
        writer.WriteLine(NamedPipeUtils.FrameEncodeVersion.ToString());

        Assert.Equal("ok", reader.ReadLine());
        Assert.Equal("3", reader.ReadLine());

        // From here the connection is framed.
        const string payload = "quote\" tab\t backslash\\";
        WriteFrame(pipe, "echo");
        WriteFrame(pipe, payload);

        Assert.Equal("ok", ReadFrame(pipe));
        Assert.Equal(payload, ReadFrame(pipe));
    }

    [Fact]
    public async Task SecondNegotiationIsRejectedAsync()
    {
        var pipeName = CreatePipeName();
        using var server = CreateServer(pipeName, e => e.Message);

        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(ConnectTimeout);

        var writer = new StreamWriter(pipe, new UTF8Encoding(false, true), 1024, true) { AutoFlush = true };
        var reader = new StreamReader(pipe, Encoding.UTF8, true, 1024, true);

        // Staying on the text version keeps this test readable.
        writer.WriteLine("--cfg-encode");
        writer.WriteLine(NamedPipeUtils.TextEncodeVersion.ToString());
        Assert.Equal("ok", reader.ReadLine());
        Assert.Equal("2", reader.ReadLine());

        writer.WriteLine("--cfg-encode");
        writer.WriteLine(NamedPipeUtils.TextEncodeVersion.ToString());

        Assert.Equal("exception", reader.ReadLine());
        Assert.Equal(typeof(InvalidOperationException).FullName, reader.ReadLine());
    }

    [Fact]
    public async Task PeerThatIgnoresTheHandshakeIsTreatedAsVersionOneAsync()
    {
        var pipeName = CreatePipeName();

        // Stands in for a 1.0 peer, which has no internal commands and answers with nothing.
        using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var serverTask = Task.Run(async () =>
        {
            await pipe.WaitForConnectionAsync();

            using var reader = new StreamReader(pipe, Encoding.UTF8, true, 1024, true);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false, true), 1024, true) { AutoFlush = true };

            while (await reader.ReadLineAsync() != null)
            {
                if (await reader.ReadLineAsync() == null)
                    break;

                await writer.WriteLineAsync("ok");
                await writer.WriteLineAsync(string.Empty);
            }
        });

        using var client = await ConnectAsync(pipeName);

        Assert.Equal(NamedPipeUtils.LegacyEncodeVersion, client.EncodeVersion);
    }

    [Fact]
    public async Task ClientThatSkipsTheHandshakeStaysOnTextAsync()
    {
        var pipeName = CreatePipeName();
        using var server = CreateServer(pipeName, e => e.Message);

        using var client = new NamedPipeClient(pipeName) { EncodeVersion = NamedPipeUtils.TextEncodeVersion };
        await client.ConnectAsync(ConnectTimeout, CancellationToken.None);

        Assert.Equal(NamedPipeUtils.TextEncodeVersion, client.EncodeVersion);
        Assert.Equal("still works", await AssertCompletesAsync(client.SendRequestAsync("echo", "still works")));
    }


    private static void WriteFrame(Stream stream, string value)
    {
        var payload = new UTF8Encoding(false, true).GetBytes(value);
        var header = new byte[4];
        header[0] = (byte)payload.Length;
        header[1] = (byte)(payload.Length >> 8);
        header[2] = (byte)(payload.Length >> 16);
        header[3] = (byte)(payload.Length >> 24);

        stream.Write(header, 0, 4);
        stream.Write(payload, 0, payload.Length);
        stream.Flush();
    }
    private static string ReadFrame(Stream stream)
    {
        var header = ReadExactly(stream, 4);
        var length = header[0] | (header[1] << 8) | (header[2] << 16) | (header[3] << 24);

        return new UTF8Encoding(false, true).GetString(ReadExactly(stream, length));
    }
    private static byte[] ReadExactly(Stream stream, int count)
    {
        var buffer = new byte[count];
        var read = 0;

        while (read < count)
        {
            var n = stream.Read(buffer, read, count - read);
            if (n == 0)
                throw new EndOfStreamException();

            read += n;
        }

        return buffer;
    }
}
