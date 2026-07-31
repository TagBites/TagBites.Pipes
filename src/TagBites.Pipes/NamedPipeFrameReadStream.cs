namespace TagBites.Pipes;

/// <summary>
/// Reads one message off the pipe, either a known number of bytes or a sequence of chunks.
/// </summary>
/// <remarks>
/// The message has to be read to the end before the connection can carry the next one,
/// so the owner drains whatever the caller left behind.
/// </remarks>
internal sealed class NamedPipeFrameReadStream : Stream
{
    private readonly Stream _stream;
    private readonly byte[] _header = new byte[4];
    private readonly bool _chunked;
    private int _remaining;
    private bool _ended;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public NamedPipeFrameReadStream(Stream stream, int length)
    {
        _stream = stream;
        _chunked = length < 0;
        _remaining = _chunked ? 0 : length;
        _ended = !_chunked && length == 0;
    }


    public override int Read(byte[] buffer, int offset, int count)
    {
        if (!BeginRead(ref count))
            return 0;

        var read = _stream.Read(buffer, offset, count);
        if (read == 0)
            throw new EndOfStreamException();

        _remaining -= read;
        return read;
    }
    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (_remaining == 0 && _chunked && !_ended)
        {
            await ReadHeaderAsync().ConfigureAwait(false);
            _remaining = GetHeaderValue();
            _ended = _remaining == 0;
        }

        if (_ended || _remaining == 0)
            return 0;

        var read = await _stream.ReadAsync(buffer, offset, Math.Min(count, _remaining), cancellationToken).ConfigureAwait(false);
        if (read == 0)
            throw new EndOfStreamException();

        _remaining -= read;
        return read;
    }

    /// <summary>
    /// Reads whatever the caller did not, so the next message starts at a message boundary.
    /// </summary>
    public async Task DrainAsync()
    {
        var buffer = new byte[8192];
        while (await ReadAsync(buffer, 0, buffer.Length, CancellationToken.None).ConfigureAwait(false) > 0)
        { }
    }

    private bool BeginRead(ref int count)
    {
        if (_remaining == 0 && _chunked && !_ended)
        {
            ReadHeader();
            _remaining = GetHeaderValue();
            _ended = _remaining == 0;
        }

        if (_ended || _remaining == 0)
            return false;

        count = Math.Min(count, _remaining);
        return true;
    }
    private void ReadHeader()
    {
        var read = 0;
        while (read < _header.Length)
        {
            var count = _stream.Read(_header, read, _header.Length - read);
            if (count == 0)
                throw new EndOfStreamException();

            read += count;
        }
    }
    private async Task ReadHeaderAsync()
    {
        var read = 0;
        while (read < _header.Length)
        {
            var count = await _stream.ReadAsync(_header, read, _header.Length - read).ConfigureAwait(false);
            if (count == 0)
                throw new EndOfStreamException();

            read += count;
        }
    }
    private int GetHeaderValue()
    {
        var value = _header[0] | (_header[1] << 8) | (_header[2] << 16) | (_header[3] << 24);
        if (value < 0)
            throw new InvalidDataException($"Chunk length {value} is negative.");

        return value;
    }

    public override void Flush()
    { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
