using System.Text;

namespace TagBites.Pipes;

/// <summary>
/// Frames a message as a length prefix followed by the bytes, so nothing has to be escaped.
/// </summary>
/// <remarks>
/// A length of <c>-1</c> starts a message of unknown size, which then travels as a sequence of chunks and ends with an empty one.
/// </remarks>
internal sealed class NamedPipeFrameChannel : NamedPipeBinaryChannel
{
    private const int HeaderLength = 4;
    private const int ChunkedLength = -1;

    private static readonly UTF8Encoding PayloadEncoding = new(false, true);

    private readonly Stream _stream;
    private readonly byte[] _readHeader = new byte[HeaderLength];
    private readonly byte[] _writeHeader = new byte[HeaderLength];

    public NamedPipeFrameChannel(Stream stream) => _stream = stream;


    public override string? Read()
    {
        if (!ReadHeader())
            return null;

        var length = GetLength();
        if (length == ChunkedLength)
            return ReadChunked();

        if (length == 0)
            return string.Empty;

        var payload = new byte[length];
        return ReadExactly(payload, length)
            ? PayloadEncoding.GetString(payload)
            : null;
    }
    public override void Write(string? value)
    {
        var payload = PayloadEncoding.GetBytes(value ?? string.Empty);
        SetLength(payload.Length);

        _stream.Write(_writeHeader, 0, HeaderLength);
        if (payload.Length > 0)
            _stream.Write(payload, 0, payload.Length);

        _stream.Flush();
    }

    public override async ValueTask<string?> ReadAsync()
    {
        if (!await ReadHeaderAsync().ConfigureAwait(false))
            return null;

        var length = GetLength();
        if (length == ChunkedLength)
            return await ReadChunkedAsync().ConfigureAwait(false);

        if (length == 0)
            return string.Empty;

        var payload = new byte[length];
        return await ReadExactlyAsync(payload, length).ConfigureAwait(false)
            ? PayloadEncoding.GetString(payload)
            : null;
    }
    public override async ValueTask WriteAsync(string? value)
    {
        var payload = PayloadEncoding.GetBytes(value ?? string.Empty);
        SetLength(payload.Length);

        await _stream.WriteAsync(_writeHeader, 0, HeaderLength).ConfigureAwait(false);
        if (payload.Length > 0)
            await _stream.WriteAsync(payload, 0, payload.Length).ConfigureAwait(false);

        await _stream.FlushAsync().ConfigureAwait(false);
    }

    public override async ValueTask<Stream?> OpenReadAsync()
    {
        if (!await ReadHeaderAsync().ConfigureAwait(false))
            return null;

        return new NamedPipeFrameReadStream(_stream, GetLength());
    }
    public override async ValueTask WriteAsync(Func<Stream, Task> write)
    {
        // A message written by a callback has no known size, so it travels as chunks.
        SetLength(ChunkedLength);
        await _stream.WriteAsync(_writeHeader, 0, HeaderLength).ConfigureAwait(false);

        var stream = new NamedPipeFrameWriteStream(_stream);
        await write(stream).ConfigureAwait(false);
        await stream.CompleteAsync().ConfigureAwait(false);
    }

    private string ReadChunked()
    {
        using var memory = new MemoryStream();
        var stream = new NamedPipeFrameReadStream(_stream, ChunkedLength);
        stream.CopyTo(memory);

        return PayloadEncoding.GetString(memory.GetBuffer(), 0, (int)memory.Length);
    }
    private async Task<string> ReadChunkedAsync()
    {
        using var memory = new MemoryStream();
        var stream = new NamedPipeFrameReadStream(_stream, ChunkedLength);
        await stream.CopyToAsync(memory).ConfigureAwait(false);

        return PayloadEncoding.GetString(memory.GetBuffer(), 0, (int)memory.Length);
    }

    private bool ReadHeader() => ReadExactly(_readHeader, HeaderLength);
    private async Task<bool> ReadHeaderAsync() => await ReadExactlyAsync(_readHeader, HeaderLength).ConfigureAwait(false);

    private bool ReadExactly(byte[] buffer, int count)
    {
        var read = 0;
        while (read < count)
        {
            var value = _stream.Read(buffer, read, count - read);
            if (value == 0)
                return false;

            read += value;
        }

        return true;
    }
    private async Task<bool> ReadExactlyAsync(byte[] buffer, int count)
    {
        var read = 0;
        while (read < count)
        {
            var value = await _stream.ReadAsync(buffer, read, count - read).ConfigureAwait(false);
            if (value == 0)
                return false;

            read += value;
        }

        return true;
    }

    private int GetLength()
    {
        var length = _readHeader[0] | (_readHeader[1] << 8) | (_readHeader[2] << 16) | (_readHeader[3] << 24);

        // Not a limit on the message, only a header that cannot be right.
        if (length < ChunkedLength)
            throw new InvalidDataException($"Message length {length} is not valid.");

        return length;
    }
    private void SetLength(int length)
    {
        _writeHeader[0] = (byte)length;
        _writeHeader[1] = (byte)(length >> 8);
        _writeHeader[2] = (byte)(length >> 16);
        _writeHeader[3] = (byte)(length >> 24);
    }

    /// <remarks>The pipe belongs to the client or the server, which closes it.</remarks>
    public override void Dispose()
    { }
}
