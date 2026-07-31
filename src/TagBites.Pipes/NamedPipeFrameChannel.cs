using System.Text;

namespace TagBites.Pipes;

/// <summary>
/// Frames a message as a length prefix followed by the UTF-8 bytes, so nothing has to be escaped.
/// </summary>
internal sealed class NamedPipeFrameChannel : NamedPipeChannel
{
    private const int HeaderLength = 4;

    private static readonly UTF8Encoding Encoding = new(false, true);

    private readonly Stream _stream;
    private readonly byte[] _readHeader = new byte[HeaderLength];
    private readonly byte[] _writeHeader = new byte[HeaderLength];

    public NamedPipeFrameChannel(Stream stream) => _stream = stream;


    public override string? Read()
    {
        var read = 0;
        while (read < HeaderLength)
        {
            var count = _stream.Read(_readHeader, read, HeaderLength - read);
            if (count == 0)
                return null;

            read += count;
        }

        var length = GetLength();
        if (length == 0)
            return string.Empty;

        var payload = new byte[length];
        read = 0;
        while (read < length)
        {
            var count = _stream.Read(payload, read, length - read);
            if (count == 0)
                return null;

            read += count;
        }

        return Encoding.GetString(payload);
    }
    public override void Write(string? value)
    {
        var payload = Encoding.GetBytes(value ?? string.Empty);
        SetLength(payload.Length);

        _stream.Write(_writeHeader, 0, HeaderLength);
        if (payload.Length > 0)
            _stream.Write(payload, 0, payload.Length);

        _stream.Flush();
    }

    public override async ValueTask<string?> ReadAsync()
    {
        var read = 0;
        while (read < HeaderLength)
        {
            var count = await _stream.ReadAsync(_readHeader, read, HeaderLength - read).ConfigureAwait(false);
            if (count == 0)
                return null;

            read += count;
        }

        var length = GetLength();
        if (length == 0)
            return string.Empty;

        var payload = new byte[length];
        read = 0;
        while (read < length)
        {
            var count = await _stream.ReadAsync(payload, read, length - read).ConfigureAwait(false);
            if (count == 0)
                return null;

            read += count;
        }

        return Encoding.GetString(payload);
    }
    public override async ValueTask WriteAsync(string? value)
    {
        var payload = Encoding.GetBytes(value ?? string.Empty);
        SetLength(payload.Length);

        await _stream.WriteAsync(_writeHeader, 0, HeaderLength).ConfigureAwait(false);
        if (payload.Length > 0)
            await _stream.WriteAsync(payload, 0, payload.Length).ConfigureAwait(false);

        await _stream.FlushAsync().ConfigureAwait(false);
    }

    private int GetLength()
    {
        var length = _readHeader[0] | (_readHeader[1] << 8) | (_readHeader[2] << 16) | (_readHeader[3] << 24);

        // Not a limit on the message, only a header that cannot be right.
        if (length < 0)
            throw new InvalidDataException($"Message length {length} is negative.");

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
