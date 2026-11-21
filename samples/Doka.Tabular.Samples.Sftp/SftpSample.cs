namespace Doka.Tabular.Samples.Sftp;

/// <summary>A customer of the agreed file layout without header.</summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public sealed record Customer(
    int Number,
    string Name,
    string City
);

/// <summary>The application's SFTP service; its streams are forward-only, like real SFTP streams.</summary>
public interface ISftpService
{
    IReadOnlyCollection<string> Files { get; }

    Task<Stream> OpenReadAsync(
        string path,
        CancellationToken cancellationToken
    );

    Task<Stream> OpenWriteAsync(
        string path,
        CancellationToken cancellationToken
    );

    Task UploadAsync(
        string path,
        Stream content,
        CancellationToken cancellationToken
    );

    Task MoveAsync(
        string path,
        string target,
        CancellationToken cancellationToken
    );
}

/// <summary>
/// SFTP import and export through the application's own service. Tabular never opens a connection; it reads or writes
/// the stream it is given and leaves it open.
/// </summary>
public static class SftpSample
{
    private static readonly TabularMap<Customer> s_map = TabularMap
        .For<Customer>()
        .Column(x => x.Number, index: 0, configure: c => c.Validate(number => number > 0))
        .Column(x => x.Name, index: 1, configure: c => c.Required())
        .Column(x => x.City, index: 3)
        .Build();

    public static async Task RunAsync(
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        ISftpService sftp = new InMemorySftpService();
        await sftp.UploadAsync("/in/customers.csv",
            new MemoryStream(Encoding.Latin1.GetBytes("1;Müller;x;Köln\n0;Schmidt;x;Bonn\n3;Weber;x;Essen")),
            cancellationToken);

        var csv = new CsvFormat(new CsvFormatOptions
        {
            Import = new CsvImportOptions { Delimiter = ';' },
            Export = new CsvExportOptions { Delimiter = ';' },
        });

        var import = await ImportAsync(sftp, csv, output, cancellationToken);
        await ExportBufferedAsync(sftp, csv, import.Items!, output, cancellationToken);
        await ExportStreamedAsync(sftp, csv, import.Items!, cancellationToken);
        await output.WriteLineAsync($"Server files: {string.Join(", ", sftp.Files.Order(StringComparer.Ordinal))}");
    }

    private static async Task<ImportReport<Customer>> ImportAsync(
        ISftpService sftp,
        CsvFormat csv,
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        // Import from a forward-only stream; the format check reads the first bytes and hands them on unchanged.
        ImportReport<Customer> import;
        await using (var source = await sftp.OpenReadAsync("/in/customers.csv", cancellationToken))
        {
            import = await csv.ImportAsync(source,
                s_map,
                new CsvImportOptions
                {
                    Strategy = ImportStrategy.Partial,
                    Encoding = Encoding.Latin1,
                },
                cancellationToken);
        }

        await sftp.MoveAsync("/in/customers.csv",
            import.Status == ReportStatus.Succeeded
                ? "/done/customers.csv"
                : "/review/customers.csv",
            cancellationToken);

        await output.WriteLineAsync(
            $"Import {import.Status}: {import.RowsImported} imported, {import.RowsRejected} rejected");

        return import;
    }

    private static async Task ExportBufferedAsync(
        ISftpService sftp,
        CsvFormat csv,
        IReadOnlyList<Customer> customers,
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        // Buffered export: the file reaches the server only when it is complete.
        var (report, file) = await csv.ExportAsync(customers, s_map, cancellationToken);
        if (file is not null)
        {
            await using (file)
            {
                await sftp.UploadAsync("/out/customers.csv", file, cancellationToken);
            }
        }

        await output.WriteLineAsync($"Buffered export {report.Status}");
    }

    private static async Task ExportStreamedAsync(
        ISftpService sftp,
        CsvFormat csv,
        IReadOnlyList<Customer> customers,
        CancellationToken cancellationToken
    )
    {
        // Streamed export: the stream output state tells whether the remote file is complete before it is renamed.
        ExportReport streamed;
        await using (var target = await sftp.OpenWriteAsync("/out/customers.part", cancellationToken))
        {
            streamed = await csv.ExportAsync(customers, s_map, target, cancellationToken);
        }

        if (streamed is { Status: ReportStatus.Succeeded, StreamOutput: StreamOutputState.Complete })
        {
            await sftp.MoveAsync("/out/customers.part", "/out/customers-streamed.csv", cancellationToken);
        }
    }

    /// <summary>Stand-in for an SFTP client: files in memory, streams without Seek, Position, and Length.</summary>
    private sealed class InMemorySftpService : ISftpService
    {
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

        public IReadOnlyCollection<string> Files => _files.Keys;

        public Task<Stream> OpenReadAsync(
            string path,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult<Stream>(new ForwardOnly(new MemoryStream(_files[path], writable: false), null));
        }

        public Task<Stream> OpenWriteAsync(
            string path,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult<Stream>(new ForwardOnly(new MemoryStream(), bytes => _files[path] = bytes));
        }

        public async Task UploadAsync(
            string path,
            Stream content,
            CancellationToken cancellationToken
        )
        {
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, cancellationToken);
            _files[path] = copy.ToArray();
        }

        public Task MoveAsync(
            string path,
            string target,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            _files[target] = _files[path];
            _files.Remove(path);

            return Task.CompletedTask;
        }

        private sealed class ForwardOnly : Stream
        {
            private readonly MemoryStream _inner;
            private readonly Action<byte[]>? _onClose;

            public ForwardOnly(
                MemoryStream inner,
                Action<byte[]>? onClose
            )
            {
                _onClose = onClose;
                _inner = inner;
            }

            public override bool CanRead => _inner.CanRead && _onClose is null;

            public override bool CanSeek => false;

            public override bool CanWrite => _onClose is not null;

            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override void Flush() { }

            public override int Read(
                byte[] buffer,
                int offset,
                int count
            ) => _inner.Read(buffer, offset, count);

            public override long Seek(
                long offset,
                SeekOrigin origin
            ) => throw new NotSupportedException();

            public override void SetLength(
                long value
            ) => throw new NotSupportedException();

            public override void Write(
                byte[] buffer,
                int offset,
                int count
            ) => _inner.Write(buffer, offset, count);

            protected override void Dispose(
                bool disposing
            )
            {
                if (disposing)
                {
                    _onClose?.Invoke(_inner.ToArray());
                    _inner.Dispose();
                }

                base.Dispose(disposing);
            }
        }
    }
}
