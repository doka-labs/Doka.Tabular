# CSV

## Dialect

The structure follows RFC 4180: comma, CRLF between records, and quotes around
fields that contain the delimiter, CR, LF, or a quote. Field content is any
Unicode text and is read and written unchanged.

| Option | Default | Effect |
| --- | --- | --- |
| `Delimiter` | `,` | Delimiter for header and fields |
| `Encoding` | UTF-8 | Invalid bytes and unrepresentable characters are encoding errors |
| `WriteBom` | `false` | Writes `EF BB BF` before UTF-8 content |
| `AllowLineBreaksInFields` | `true` | `false`: every line break ends the record |
| `RequireCrLf` | `false` | `true`: a single LF between records is a syntax error |
| `EmptyLinesAsRecords` | `false` | `true`: an empty line is a record with one empty field |

A BOM at the start of a UTF-8 file is removed on import. Windows-1252 needs an
`Encoding` instance from `CodePagesEncodingProvider`, provided by the
application.

## Header modes

| `Binding` | First row |
| --- | --- |
| `ByIndex` (default) | A record |
| `ByIndexWithHeader` | A header, ignored for binding |
| `ByHeaderName` | A header, columns are found by their headers |

Exports write a header only with `WriteHeader = true`.

## Records and width

Every record must reach the highest mapped index; additional fields are
allowed. A short record is `tabular.schema.missingColumn` for that record. An
unclosed quote with line breaks allowed ends the run with a parser error; with
`AllowLineBreaksInFields = false` only that record is rejected.

## Format check

A CSV import whose first bytes are a ZIP or CFB container signature ends with
`tabular.schema.formatMismatch` before the first record.
