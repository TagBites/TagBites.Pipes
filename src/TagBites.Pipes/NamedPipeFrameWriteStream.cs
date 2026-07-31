namespace TagBites.Pipes;

/// <summary>
/// Writes one message onto the pipe as a sequence of chunks, so the size does not have to be known in advance.
/// </summary>
internal sealed class NamedPipeFrameWriteStream : Stream
{
    private const int BufferLength = 64 * 1024;

    private readonly Stream _stream;
    private readonly byte[] _header = new byte[4];
    private readonly byte[] _buffer = new byte[BufferLength];
    private int _length;

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public NamedPipeFrameWriteStream(Stream stream) => _stream = stream;


    public override void Write(byte[] buffer, int offset, int count)
    {
        while (count > 0)
        {
            var take = Math.Min(count, BufferLength - _length);
            Array.Copy(buffer, offset, _buffer, _length, take);

            _length += take;
            offset += take;
            count -= take;

            if (_length == BufferLength)
                WriteChunk(_length);
        }
    }
    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        while (count > 0)
        {
            var take = Math.Min(count, BufferLength - _length);
            Array.Copy(buffer, offset, _buffer, _length, take);

            _length += take;
            offset += take;
            count -= take;

            if (_length == BufferLength)
                await WriteChunkAsync(_length).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Writes what is left and closes the message with an empty chunk.
    /// </summary>
    public async Task CompleteAsync()
    {
        if (_length > 0)
            await WriteChunkAsync(_length).ConfigureAwait(false);

        SetHeader(0);
        await _stream.WriteAsync(_header, 0, _header.Length).ConfigureAwait(false);
        await _stream.FlushAsync().ConfigureAwait(false);
    }

    private void WriteChunk(int length)
    {
        SetHeader(length);
        _stream.Write(_header, 0, _header.Length);
        _stream.Write(_buffer, 0, length);
        _length = 0;
    }
    private async Task WriteChunkAsync(int length)
    {
        SetHeader(length);
        await _stream.WriteAsync(_header, 0, _header.Length).ConfigureAwait(false);
        await _stream.WriteAsync(_buffer, 0, length).ConfigureAwait(false);
        _length = 0;
    }
    private void SetHeader(int length)
    {
        _header[0] = (byte)length;
        _header[1] = (byte)(length >> 8);
        _header[2] = (byte)(length >> 16);
        _header[3] = (byte)(length >> 24);
    }

    public override void Flush()
    { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
