namespace TagBites.Pipes;

/// <summary>
/// Frames a message as one line of text, escaped with the agreed encoding version.
/// </summary>
internal sealed class NamedPipeTextChannel : NamedPipeChannel
{
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;

    public int EncodeVersion { get; set; }

    public NamedPipeTextChannel(Stream stream)
    {
        _reader = new StreamReader(stream);
        _writer = new StreamWriter(stream) { AutoFlush = true };
    }


    public override string? Read() => Decode(_reader.ReadLine());
    public override void Write(string? value) => _writer.WriteLine(Encode(value));

    public override async ValueTask<string?> ReadAsync() => Decode(await _reader.ReadLineAsync().ConfigureAwait(false));
    public override async ValueTask WriteAsync(string? value) => await _writer.WriteLineAsync(Encode(value)).ConfigureAwait(false);

    private string Encode(string? value) => NamedPipeUtils.GetEncoder(EncodeVersion)(value);
    private string? Decode(string? line) => line != null ? NamedPipeUtils.GetDecoder(EncodeVersion)(line) : null;

    public override void Dispose()
    {
        _writer.Dispose();
        _reader.Dispose();
    }
}
