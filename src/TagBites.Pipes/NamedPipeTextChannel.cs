using System.Text;

namespace TagBites.Pipes;

/// <summary>
/// Frames a message as one line of text, escaped with the agreed encoding version.
/// </summary>
internal sealed class NamedPipeTextChannel : NamedPipeChannel
{
    private static readonly UTF8Encoding PayloadEncoding = new(false, true);

    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;

    public int EncodeVersion { get; set; }

    public NamedPipeTextChannel(Stream stream)
    {
        // The pipe outlives this channel, because a connection can move on to another version.
        _reader = new StreamReader(stream, Encoding.UTF8, true, 1024, true);
        _writer = new StreamWriter(stream, PayloadEncoding, 1024, true) { AutoFlush = true };
    }


    public override string? Read() => Decode(_reader.ReadLine());
    public override void Write(string? value) => _writer.WriteLine(Encode(value));

    public override async ValueTask<string?> ReadAsync() => Decode(await _reader.ReadLineAsync().ConfigureAwait(false));
    public override async ValueTask WriteAsync(string? value) => await _writer.WriteLineAsync(Encode(value)).ConfigureAwait(false);

    /// <remarks>The message is already a string here, so the stream just reads it back.</remarks>
    public override async ValueTask<Stream?> OpenReadAsync()
    {
        var value = await ReadAsync().ConfigureAwait(false);

        return value != null
            ? new MemoryStream(PayloadEncoding.GetBytes(value), false)
            : null;
    }

    private string Encode(string? value) => NamedPipeUtils.GetEncoder(EncodeVersion)(value);
    private string? Decode(string? line) => line != null ? NamedPipeUtils.GetDecoder(EncodeVersion)(line) : null;

    public override void Dispose()
    {
        _writer.Dispose();
        _reader.Dispose();
    }
}
