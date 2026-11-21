namespace Doka.Tabular.Csv;

internal sealed partial class CsvRecordSource
{
    private async ValueTask<RecordReadResult> ReadFieldsAsync(
        long recordLine,
        CancellationToken cancellationToken
    )
    {
        while (true)
        {
            var quoted = await _reader
                    .EnsureAsync(cancellationToken)
                    .ConfigureAwait(false)
                && _reader.Buffered[0] == Quote;

            var ended = quoted
                ? await ReadQuotedFieldAsync(recordLine, cancellationToken)
                    .ConfigureAwait(false)
                : await ReadUnquotedFieldAsync(recordLine, cancellationToken)
                    .ConfigureAwait(false);

            if (ended is { } result)
            {
                return result;
            }
        }
    }

    /// <summary>Reads a quoted field; the result ends the record, <see langword="null" /> continues after a delimiter.</summary>
    private async ValueTask<RecordReadResult?> ReadQuotedFieldAsync(
        long recordLine,
        CancellationToken cancellationToken
    )
    {
        var fieldLine = _line;
        _reader.Advance(1);
        var start = _text.Length;
        if (await ReadQuotedTextAsync(recordLine, fieldLine, _cells.Count, start, cancellationToken)
                .ConfigureAwait(false) is { } broken)
        {
            return broken;
        }

        AddField(start, fieldLine);
        var after = await _reader
            .PeekAsync(cancellationToken)
            .ConfigureAwait(false);

        if (after == _settings.Delimiter)
        {
            _reader.Advance(1);

            return null;
        }

        // WHY: Each branch handles one scanner position with its own state change; a switch adds no clarity.
        // ReSharper disable once ConvertIfStatementToSwitchStatement
        if (after is -1)
        {
            _ended = true;

            return Record(recordLine);
        }

        if (after is '\r' or '\n')
        {
            return await EndRecordAsync(recordLine, cancellationToken)
                .ConfigureAwait(false);
        }

        await SkipLineAsync(cancellationToken)
            .ConfigureAwait(false);

        return RecordReadResult.Broken(SyntaxError(fieldLine,
            _cells.Count - 1,
            "Characters follow the closing quote of a field."));
    }

    /// <summary>Reads the text up to the closing quote; a result is returned only when the field cannot be read.</summary>
    private async ValueTask<RecordReadResult?> ReadQuotedTextAsync(
        long recordLine,
        long fieldLine,
        int column,
        int start,
        CancellationToken cancellationToken
    )
    {
        while (true)
        {
            if (!await _reader
                .EnsureAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                _ended = true;

                return _settings.AllowLineBreaksInFields
                    ? RecordReadResult.FatalError(SyntaxError(fieldLine,
                        column,
                        "A quoted field is not closed before the end of the input."))
                    : RecordReadResult.Broken(SyntaxError(fieldLine, column, "A quoted field is not closed."));
            }

            var buffered = _reader.Buffered;
            var stop = buffered.IndexOfAny(_quotedStops);
            if (stop < 0)
            {
                Append(buffered, start);
                _reader.Advance(buffered.Length);
                continue;
            }

            Append(buffered[..stop], start);
            var character = buffered[stop];
            _reader.Advance(stop + 1);
            if (character == Quote)
            {
                if (await _reader
                        .PeekAsync(cancellationToken)
                        .ConfigureAwait(false)
                    != Quote)
                {
                    return null;
                }

                _reader.Advance(1);
                Append([Quote], start);
            }
            else if (await AppendQuotedLineBreakAsync(character, recordLine, column, start, cancellationToken)
                    .ConfigureAwait(false) is { } broken)
            {
                return broken;
            }
        }
    }

    /// <summary>Keeps a line break inside a quoted field, or rejects the record when line breaks are not allowed.</summary>
    private async ValueTask<RecordReadResult?> AppendQuotedLineBreakAsync(
        char character,
        long recordLine,
        int column,
        int start,
        CancellationToken cancellationToken
    )
    {
        if (!_settings.AllowLineBreaksInFields)
        {
            await FinishLineBreakAsync(character, cancellationToken)
                .ConfigureAwait(false);

            return RecordReadResult.Broken(SyntaxError(recordLine,
                column,
                "A line break inside a quoted field is not allowed."));
        }

        Append([character], start);
        if (character == '\r'
            && await _reader
                .PeekAsync(cancellationToken)
                .ConfigureAwait(false)
            == '\n')
        {
            _reader.Advance(1);
            Append(['\n'], start);
        }

        _line++;

        return null;
    }

    /// <summary>Reads an unquoted field; the result ends the record, <see langword="null" /> continues after a delimiter.</summary>
    private async ValueTask<RecordReadResult?> ReadUnquotedFieldAsync(
        long recordLine,
        CancellationToken cancellationToken
    )
    {
        var start = _text.Length;
        while (true)
        {
            if (!await _reader
                .EnsureAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                AddField(start, _line);
                _ended = true;

                return Record(recordLine);
            }

            var buffered = _reader.Buffered;
            var stop = buffered.IndexOfAny(_unquotedStops);
            if (stop < 0)
            {
                Append(buffered, start);
                _reader.Advance(buffered.Length);
                continue;
            }

            Append(buffered[..stop], start);
            var character = buffered[stop];
            _reader.Advance(stop);
            if (character == _settings.Delimiter)
            {
                _reader.Advance(1);
                AddField(start, _line);

                return null;
            }

            if (character is '\r' or '\n')
            {
                AddField(start, _line);

                return await EndRecordAsync(recordLine, cancellationToken)
                    .ConfigureAwait(false);
            }

            // WHY: An unquoted field holds no line break; it starts on the current line.
            var fieldLine = _line;
            await SkipLineAsync(cancellationToken)
                .ConfigureAwait(false);

            return RecordReadResult.Broken(SyntaxError(fieldLine,
                _cells.Count,
                "A quote appears inside an unquoted field."));
        }
    }
}
