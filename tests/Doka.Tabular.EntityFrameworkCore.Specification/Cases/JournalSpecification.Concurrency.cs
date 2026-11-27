namespace Doka.Tabular.EntityFrameworkCore.Specification;

/// <summary>Two imports of the same file at the same time: exactly one processes it.</summary>
public abstract partial class JournalSpecification
{
    [Fact]
    public async Task TwoImportsOfTheSameFileAtOnce_ProcessItExactlyOnce()
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        var journal = TabularJournal.ForFactory(database.Factory);
        var bothHashed = new Barrier(2);

        // Act
        var reports = await Task.WhenAll(
            Task.Run(() => ImportFileAsync(bothHashed), Token),
            Task.Run(() => ImportFileAsync(bothHashed), Token));

        // Assert
        var decisions = reports
            .Select(report => report.Journal!.Decision)
            .Order()
            .ToList();

        Assert.Equal(ImportJournalDecision.Processed, decisions[0]);
        Assert.Contains(decisions[1], new[] { ImportJournalDecision.Running, ImportJournalDecision.Skipped });
        Assert.Equal(2, reports.Sum(report => report.RowsImported));
        Assert.Equal(2, (await database.UsersAsync()).Count);

        return;

        Task<ImportReport<UserDto>> ImportFileAsync(
            Barrier barrier
        ) => s_csv.ImportAsync(
            new ReadThenWait(TestMaps.Csv([.. TestMaps.Rows(2)]), barrier),
            TestMaps.Users,

            // ReSharper disable once AccessToDisposedClosure
            // WHY: Both imports are awaited before the database is disposed.
            TestMaps
                .Upsert(TabularDatabase.ForFactory(database.Factory))
                .WithImportLog(journal, new TabularImportLogOptions { Scope = JournalScope.Create("users") }),
            Token);
    }

    /// <summary>
    /// A stream that waits at its end until the other import has read its file too, so both take the claim after
    /// hashing at about the same time.
    /// </summary>
    private sealed class ReadThenWait : Stream
    {
        private readonly Barrier _barrier;
        private readonly Stream _inner;
        private bool _waited;

        public ReadThenWait(
            Stream inner,
            Barrier barrier
        )
        {
            _inner = inner;
            _barrier = barrier;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count
        )
        {
            var read = _inner.Read(buffer, offset, count);
            if (read == 0
                && !_waited)
            {
                _waited = true;
                _barrier.SignalAndWait(TestContext.Current.CancellationToken);
            }

            return read;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            var read = await _inner.ReadAsync(buffer, cancellationToken);
            if (read == 0
                && !_waited)
            {
                _waited = true;
                await Task.Run(() => _barrier.SignalAndWait(cancellationToken), cancellationToken);
            }

            return read;
        }

        public override void Flush() { }

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
        ) => throw new NotSupportedException();
    }
}
