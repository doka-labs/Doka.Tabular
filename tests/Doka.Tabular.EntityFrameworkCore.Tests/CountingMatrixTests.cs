using System.Linq;
using Doka.Tabular.Csv;
using Doka.Tabular.Excel;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Doka.Tabular.EntityFrameworkCore.Tests;

/// <summary>
/// The counters of a report match the database: whatever happens at a row and whatever fails afterwards, the rows in
/// the database lie between the imported (and, joined, the pending) rows and those plus the uncertain ones.
/// </summary>
public sealed class CountingMatrixTests
{
    public enum RowEvent
    {
        None,
        DataError,
        Technical,
        Cancel,
    }

    private const int Rows = 6;
    private const int EventRow = 4;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static TheoryData<bool, ImportStrategy, bool, int, RowEvent, Failure> Scenarios()
    {
        var data = new TheoryData<bool, ImportStrategy, bool, int, RowEvent, Failure>();
        foreach (var workbook in new[] { false, true })
        {
            foreach (var strategy in new[] { ImportStrategy.Strict, ImportStrategy.Partial })
            {
                foreach (var joined in new[] { false, true })
                {
                    foreach (var group in joined || strategy == ImportStrategy.Strict
                        ? new[] { 1 }
                        : new[] { 1, 2 })
                    {
                        foreach (var rowEvent in Enum.GetValues<RowEvent>())
                        {
                            foreach (var failure in Enum.GetValues<Failure>())
                            {
                                data.Add(workbook, strategy, joined, group, rowEvent, failure);
                            }
                        }
                    }
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task Counters_MatchTheDatabase(
        bool workbook,
        ImportStrategy strategy,
        bool joined,
        int groupSize,
        RowEvent rowEvent,
        Failure failure
    )
    {
        // Arrange
        await using var database = await SqliteFile.OpenAsync(Token);
        var connection = database.Connection;
        await using (var setup = new TabularTestContext(Options(connection)))
        {
            await setup.Database.EnsureCreatedAsync(Token);
        }

        var faults = new TransactionFaults(failure);
        await using var context = new TabularTestContext(new DbContextOptionsBuilder<TabularTestContext>()
            .UseSqlite(connection)
            .AddInterceptors(faults)
            .Options);

        var transaction = joined
            ? await context.Database.BeginTransactionAsync(Token)
            : null;

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var target = TabularDatabase
            .ForContext(context)
            .PrepareUpsert<UserDto, User>(set: users => users.Users,
                match: TestMaps.UserMatch,
                create: dto =>
                {
                    if (dto.PersonnelNumber == EventRow)
                    {
                        faults.Armed = true;
                        switch (rowEvent)
                        {
                            case RowEvent.DataError:
                                throw new InvalidUserException("The row is invalid.");
                            case RowEvent.Technical:
                                throw new IOException("The row failed technically.");
                            case RowEvent.Cancel:
                                // ReSharper disable once AccessToDisposedClosure
                                // WHY: The callback runs during the awaited import, before the source is disposed.
                                cancellation.Cancel();
                                break;
                        }
                    }

                    return User.Create(dto.PersonnelNumber, dto.Email, dto.Name);
                },
                update: (_, _) => { },
                errors: errors => errors.Expect<InvalidUserException>(TestMaps.UserInvalid))
            .WithOptions(joined
                ? new TabularWriteOptions { JoinExistingTransaction = true }
                : new TabularWriteOptions { CommitGroupSize = groupSize });

        // Act
        // WHY: A joined import ends with the commit of the application, so the import and that commit form one action.
        var report = workbook
            ? await ImportWorkbookAsync(target, strategy, cancellation.Token)
            : Counts.Of(await new CsvFormat(new CsvFormatOptions()).ImportAsync(
                TestMaps.Csv([.. TestMaps.Rows(Rows)]),
                TestMaps.Users,
                target,
                new CsvImportOptions { Strategy = strategy },
                cancellation.Token));

        faults.Armed = false;
        if (transaction is not null)
        {
            await transaction.CommitAsync(Token);
            await transaction.DisposeAsync();
        }

        // Assert
        await using var check = new TabularTestContext(Options(connection));
        var stored = await check.Users.CountAsync(Token);
        var certain = report.RowsImported
            + (joined
                ? report.RowsPendingCommit
                : 0);

        var label =
            $"workbook={workbook}/{strategy}/joined={joined}/group={groupSize}/{rowEvent}/{failure}: status={report.Status} "
            + $"imported={report.RowsImported} pending={report.RowsPendingCommit} uncertain={report.RowsUncertain} "
            + $"rejected={report.RowsRejected} stored={stored} fatal={report.FatalError?.Code.Value}";

        // WHY: An own transaction commits after the rows and rolls back on a technical error; a scenario with such a
        // failure proves only something when the failure was injected.
        var mustInject = !joined
            && failure switch
            {
                Failure.CommitRejected or Failure.CommitThrowsAfterwards => rowEvent == RowEvent.None,
                Failure.RollbackFails => rowEvent == RowEvent.Technical,
                _ => false,
            };

        Assert.True(!mustInject || faults.Injected > 0, label + " injected=0");
        Assert.True(stored >= certain && stored <= certain + report.RowsUncertain, label);
        Assert.True(report.RowsImported
            + report.RowsPendingCommit
            + report.RowsUncertain
            + report.RowsRejected
            + report.RowsSkipped
            <= report.RowsRead,
            label);

        Assert.True(report.RowsUncertain == 0
            || report.FatalError?.Code == TabularErrorCodes.Persistence.CommitUnknown
            || report.FatalError?.Code == TabularErrorCodes.Persistence.RollbackUnknown,
            label);

        Assert.True(rowEvent != RowEvent.None
            || failure != Failure.None
            || (report.Status == ReportStatus.Succeeded && stored == Rows),
            label);
    }

    /// <summary>Rows 1 to 3 on the first sheet, rows 4 to 6 on the second, both written to the same target.</summary>
    private static async Task<Counts> ImportWorkbookAsync(
        TabularWriteTarget<UserDto> target,
        ImportStrategy strategy,
        CancellationToken cancellationToken
    )
    {
        var excel = new ExcelFormat(new ExcelFormatOptions());
        var users = Enumerable
            .Range(1, Rows)
            .Select(number => new UserDto(number, $"user{number}@example.org", $"User {number}"))
            .ToArray();

        var export = ExcelExport.Create();
        export.Add(TabularSheetName.Create("First"), users[..3], TestMaps.Users);
        export.Add(TabularSheetName.Create("Second"), users[3..], TestMaps.Users);
        var file = new MemoryStream();
        await excel.ExportAsync(export.Build(), file, Token);
        file.Position = 0;
        var sheets = ExcelImport.Create();
        sheets.Add(TabularSheetName.Create("First"), TestMaps.Users, target);
        sheets.Add(TabularSheetName.Create("Second"), TestMaps.Users, target);

        var report = await excel.ImportAsync(file,
            sheets.Build(),
            new ExcelWorkbookImportOptions { Strategy = strategy },
            cancellationToken);

        return new Counts(report.Status,
            report.RowsRead,
            report.RowsImported,
            report.RowsPendingCommit,
            report.RowsUncertain,
            report.RowsRejected,
            report.RowsSkipped,
            report.FatalError);
    }

    private static DbContextOptions<TabularTestContext> Options(
        SqliteConnection connection
    ) => new DbContextOptionsBuilder<TabularTestContext>()
        .UseSqlite(connection)
        .Options;

    /// <summary>The counters of a single-file or a workbook report.</summary>
    private sealed record Counts(
        ReportStatus Status,
        long RowsRead,
        long RowsImported,
        long RowsPendingCommit,
        long RowsUncertain,
        long RowsRejected,
        long RowsSkipped,
        TabularError? FatalError
    )
    {
        public static Counts Of(
            ImportReport<UserDto> report
        ) => new(report.Status,
            report.RowsRead,
            report.RowsImported,
            report.RowsPendingCommit,
            report.RowsUncertain,
            report.RowsRejected,
            report.RowsSkipped,
            report.FatalError);
    }
}
