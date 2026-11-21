# Web and file transfer

## Buffered download

Decide the response before the first byte:

```csharp
var (report, file) = await export.ExportAsync(rows, map, request.Type, options, cancellationToken);
return file is null
    ? Results.UnprocessableEntity(report.Errors.Select(error => error.Code.Value))
    : Results.File(file, request.Type.ContentType, $"contacts{request.Type.FileExtension}");
```

`Results.File` and `ControllerBase.File` dispose the stream after sending, which
also removes a spilled temporary file.

## Streamed download

```csharp
http.Response.ContentType = TabularType.Excel.ContentType;
var report = await export.ExportAsync(rows, map, http.Response.Body, TabularType.Excel, cancellationToken);
if (report.Status == ReportStatus.Succeeded)
{
    return;
}

if (!http.Response.HasStarted)
{
    http.Response.ContentType = null;
    await Results
        .UnprocessableEntity(report.Errors.Select(error => error.Code.Value))
        .ExecuteAsync(http);

    return;
}

http.Abort();
```

Bytes leave while the source is read; after the first byte the status code is
fixed. A failure before the first byte, such as an invalid column selection,
still gets an error response instead of an empty download. The Excel writer
never writes synchronously to the response body.

## Upload

The client states the format; Tabular checks it against the first bytes and
ignores the file name:

```csharp
await using var source = file.OpenReadStream();
var report = await import.ImportAsync(source, type, map, cancellationToken);
```

## SFTP

The application opens the SFTP streams; Tabular reads or writes them and leaves
them open. Forward-only streams work. Upload a buffered export to make the
remote file appear only when it is complete, or stream and rename after
`StreamOutput == StreamOutputState.Complete`.

See the samples in `samples/` for complete programs.
