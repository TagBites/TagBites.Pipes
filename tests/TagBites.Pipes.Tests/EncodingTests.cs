namespace TagBites.Pipes.Tests;

/// <summary>
/// A released encoding version is a wire contract - its output must never change.
/// </summary>
public class EncodingTests : PipeTestBase
{
    [Theory]
    [InlineData("", "")]
    [InlineData("plain", "plain")]
    [InlineData("trailing spaces   ", "trailing spaces")]
    [InlineData("\ttabbed\t", "\ttabbed")]
    [InlineData("line\nbreak", "line\\nbreak")]
    [InlineData("carriage\rreturn", "carriage\\rreturn")]
    [InlineData("back\\slash", "back\\slash")]
    [InlineData("quote'and\"quote", "quote'and\"quote")]
    public void LegacyEncodingWireFormat(string value, string expected) => AssertEncodes(NamedPipeUtils.LegacyEncodeVersion, value, expected);

    [Theory]
    [InlineData("", "")]
    [InlineData("plain", "plain")]
    [InlineData("trailing spaces   ", "trailing spaces   ")]
    [InlineData("\ttabbed\t", "\\ttabbed\\t")]
    [InlineData("line\nbreak", "line\\nbreak")]
    [InlineData("carriage\rreturn", "carriage\\rreturn")]
    [InlineData("back\\slash", "back\\\\slash")]
    [InlineData("quote'and\"quote", "quote\\'and\\\"quote")]
    [InlineData("null\0char", "null\\0char")]
    public void TextEncodingWireFormat(string value, string expected) => AssertEncodes(NamedPipeUtils.TextEncodeVersion, value, expected);

    /// <summary>
    /// Version 1 escapes line breaks only - backslashes and trailing whitespace do not round trip.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("plain")]
    [InlineData("line\nbreak")]
    [InlineData("carriage\rreturn")]
    [InlineData("\n\r\r\n")]
    public void LegacyEncodingRoundTrip(string value) => AssertRoundTrip(NamedPipeUtils.LegacyEncodeVersion, value);

    [Theory]
    [InlineData("")]
    [InlineData("plain")]
    [InlineData("trailing spaces   ")]
    [InlineData("\ttabbed\t")]
    [InlineData("line\nbreak")]
    [InlineData("carriage\rreturn")]
    [InlineData("\n\r\r\n")]
    [InlineData("back\\slash")]
    [InlineData("double\\\\slash")]
    [InlineData("quote'and\"quote")]
    [InlineData("null\0char")]
    [InlineData("escaped\\nnewline\n")]
    public void TextEncodingRoundTrip(string value) => AssertRoundTrip(NamedPipeUtils.TextEncodeVersion, value);

    [Fact]
    public async Task LegacyConnectionDropsTrailingWhitespaceAsync()
    {
        var pipeName = CreatePipeName();

        using var server = new NamedPipeServer(pipeName) { SupportLegacyEncoding = true };
        server.Request += (_, e) => e.Response = e.Message;
        server.Enabled = true;

        // Skips the handshake
        using var client = new NamedPipeClient(pipeName) { EncodeVersion = NamedPipeUtils.LegacyEncodeVersion };
        await client.ConnectAsync(ConnectTimeout, CancellationToken.None);

        // Intentional - version 1 has always dropped it
        Assert.Equal("value", await AssertCompletesAsync(client.SendRequestAsync("echo", "value   ")));
    }

    [Fact]
    public async Task CurrentConnectionKeepsTrailingWhitespaceAsync()
    {
        var pipeName = CreatePipeName();
        using var server = CreateServer(pipeName, e => e.Message);
        using var client = await ConnectAsync(pipeName);

        const string payload = "value with trailing spaces   ";
        Assert.Equal(payload, await AssertCompletesAsync(client.SendRequestAsync("echo", payload)));
    }

    [Fact]
    public async Task NegotiatedConnectionUsesCurrentEncodingAsync()
    {
        var pipeName = CreatePipeName();
        using var server = CreateServer(pipeName, e => e.Message);

        using var client = await ConnectAsync(pipeName);

        Assert.Equal(NamedPipeUtils.CurrentEncodeVersion, client.EncodeVersion);
    }

    [Fact]
    public async Task LegacyServerStillNegotiatesCurrentEncodingAsync()
    {
        var pipeName = CreatePipeName();

        using var server = new NamedPipeServer(pipeName) { SupportLegacyEncoding = true };
        server.Request += (_, e) => e.Response = e.Message;
        server.Enabled = true;

        using var client = await ConnectAsync(pipeName);

        Assert.Equal(NamedPipeUtils.CurrentEncodeVersion, client.EncodeVersion);
    }


    private static void AssertEncodes(int version, string value, string expected)
    {
        var encoded = NamedPipeUtils.GetEncoder(version)(value);

        Assert.Equal(expected, encoded);
    }
    private static void AssertRoundTrip(int version, string value)
    {
        var encoded = NamedPipeUtils.GetEncoder(version)(value);

        // A line break would split the message
        Assert.DoesNotContain("\n", encoded);
        Assert.DoesNotContain("\r", encoded);

        Assert.Equal(value, NamedPipeUtils.GetDecoder(version)(encoded));
    }
}
