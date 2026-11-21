using System.Text;

namespace Doka.Tabular.Csv;

/// <summary>Decodes characters from a byte stream; invalid byte sequences are errors, never replaced.</summary>
internal sealed class CharReader
{
    private readonly byte[] _bytes = new byte[32768];
    private readonly char[] _chars = new char[32768];
    private readonly Decoder _decoder;
    private readonly Stream _stream;
    private int _byteCount;
    private int _byteOffset;
    private bool _endOfBytes;
    private bool _flushed;
    private int _length;
    private int _position;

    public CharReader(
        Stream stream,
        Encoding encoding,
        ReadOnlyMemory<byte> prefix
    )
    {
        _stream = stream;
        var strict = (Encoding)encoding.Clone();
        strict.DecoderFallback = DecoderFallback.ExceptionFallback;
        _decoder = strict.GetDecoder();
        Prefix = prefix;
    }

    private ReadOnlyMemory<byte> Prefix { get; set; }

    /// <summary>Gets the decoded characters not yet consumed.</summary>
    public ReadOnlySpan<char> Buffered => _chars.AsSpan(_position, _length - _position);

    /// <summary>Consumes characters of <see cref="Buffered" />.</summary>
    public void Advance(
        int count
    ) => _position += count;

    /// <summary>Makes characters available; <see langword="false" /> at the end of the input.</summary>
    public ValueTask<bool> EnsureAsync(
        CancellationToken cancellationToken
    ) => _position < _length
        ? ValueTask.FromResult(true)
        : FillAsync(cancellationToken);

    public async ValueTask<int> PeekAsync(
        CancellationToken cancellationToken
    )
    {
        if (_position < _length
            || await FillAsync(cancellationToken)
                .ConfigureAwait(false))
        {
            return _chars[_position];
        }

        return -1;
    }

    public async ValueTask<int> ReadAsync(
        CancellationToken cancellationToken
    )
    {
        if (_position < _length
            || await FillAsync(cancellationToken)
                .ConfigureAwait(false))
        {
            return _chars[_position++];
        }

        return -1;
    }

    private async ValueTask<bool> FillAsync(
        CancellationToken cancellationToken
    )
    {
        while (true)
        {
            _position = 0;
            _length = 0;
            if (_byteOffset == _byteCount)
            {
                if (_endOfBytes)
                {
                    return Flush();
                }

                _byteOffset = 0;
                _byteCount = await ReadBytesAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (_byteCount == 0)
                {
                    _endOfBytes = true;
                    continue;
                }
            }

            // WHY: The decoder may fill the character buffer before it consumed every byte, for example when
            // it still holds the start of a scalar from the previous block; the rest is decoded next time.
            _decoder.Convert(_bytes,
                _byteOffset,
                _byteCount - _byteOffset,
                _chars,
                0,
                _chars.Length,
                flush: false,
                out var used,
                out var produced,
                out _);

            _byteOffset += used;
            _length = produced;
            if (_length > 0)
            {
                return true;
            }
        }
    }

    /// <summary>Decodes the characters the decoder still holds after the last byte; once only.</summary>
    private bool Flush()
    {
        if (_flushed)
        {
            return false;
        }

        _flushed = true;
        _decoder.Convert([],
            0,
            0,
            _chars,
            0,
            _chars.Length,
            flush: true,
            out _,
            out var tail,
            out _);

        _length = tail;

        return _length > 0;
    }

    /// <summary>Reads the next bytes: first the already read prefix, then the stream.</summary>
    private async ValueTask<int> ReadBytesAsync(
        CancellationToken cancellationToken
    )
    {
        if (Prefix.IsEmpty)
        {
            return await _stream
                .ReadAsync(_bytes, cancellationToken)
                .ConfigureAwait(false);
        }

        var read = Prefix.Length;
        Prefix.CopyTo(_bytes);
        Prefix = ReadOnlyMemory<byte>.Empty;

        return read;
    }
}
