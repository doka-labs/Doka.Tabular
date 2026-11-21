# Resources and limits

Limits are set in the format options and apply per call.

| Option | Default | Effect |
| --- | --- | --- |
| `ImportBufferBytes` | 16 MiB | Input kept in memory, then a temporary file |
| `OutputBufferBytes` | 16 MiB | Buffered exports and stored files |
| `AllowSpill` | `true` | `false` forbids temporary files for buffers |
| `TemporaryDirectory` | system temporary directory | Location of own temporary files |
| `MaxTemporaryBytes` | none | Sum of concurrent temporary files of a call |
| `MaxInputBytes` | none | Bytes read from the input |
| `MaxFieldCharacters` | none | UTF-16 code units per field, also unmapped |
| `MaxExpandedBytes` (Excel) | 1 GiB | Bytes unpacked from a workbook |

A reached limit is the technical error `tabular.resource.limitExceeded`.

## Streams and paths

Passed streams stay open; paths are opened and closed by the library. Inputs
may be forward-only; when a second read is needed, the input is buffered within
the limits above. Exports into a path write a temporary file next to the target
and publish it with an atomic rename; a failure leaves an existing file
unchanged. Exports into a stream write while the source is read;
`StreamOutput` tells whether the output is complete or partial.

Own temporary files are removed on success, failure, and cancellation. A
buffered file handed to the caller is removed when its stream is disposed.
