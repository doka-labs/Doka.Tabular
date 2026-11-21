namespace Doka.Tabular;

/// <summary>
/// Character buffer that one reader or writer owns exclusively and reuses for every row. Views returned by
/// <see cref="Slice" /> stay valid until <see cref="Clear" />; growing the buffer moves new text to a larger array,
/// so earlier views keep their content.
/// </summary>
internal sealed class TextBuffer
{
    private char[] _chars;

    public TextBuffer(
        int capacity = 256
    )
    {
        _chars = new char[capacity];
    }

    public int Length { get; private set; }

    public void Clear() => Length = 0;

    /// <summary>Drops the text written after <paramref name="length" />.</summary>
    public void Truncate(
        int length
    ) => Length = length;

    public Span<char> GetSpan(
        int sizeHint
    )
    {
        Ensure(sizeHint);

        return _chars.AsSpan(Length);
    }

    public void Advance(
        int count
    ) => Length += count;

    public void Append(
        char character
    )
    {
        Ensure(1);
        _chars[Length++] = character;
    }

    public void Append(
        char character,
        int count
    )
    {
        Ensure(count);
        _chars
            .AsSpan(Length, count)
            .Fill(character);

        Length += count;
    }

    public void Append(
        ReadOnlySpan<char> text
    )
    {
        Ensure(text.Length);
        text.CopyTo(_chars.AsSpan(Length));
        Length += text.Length;
    }

    /// <summary>Appends a formatted value; the buffer grows until the value fits.</summary>
    public void AppendFormatted<TValue>(
        TValue value,
        ReadOnlySpan<char> format,
        IFormatProvider? provider
    )
        where TValue : ISpanFormattable
    {
        var size = 32;
        while (true)
        {
            if (value.TryFormat(GetSpan(size), out var written, format, provider))
            {
                Length += written;

                return;
            }

            size *= 2;
        }
    }

    /// <summary>Gets the text written since <paramref name="start" /> as a view.</summary>
    public ReadOnlyMemory<char> Slice(
        int start
    ) => _chars.AsMemory(start, Length - start);

    /// <summary>Gets the text written since <paramref name="start" /> for in-place changes.</summary>
    public Span<char> Written(
        int start
    ) => _chars.AsSpan(start, Length - start);

    private void Ensure(
        int additional
    )
    {
        if (Length + additional <= _chars.Length)
        {
            return;
        }

        var larger = new char[Math.Max(_chars.Length * 2, Length + additional)];
        _chars
            .AsSpan(0, Length)
            .CopyTo(larger);

        _chars = larger;
    }
}
