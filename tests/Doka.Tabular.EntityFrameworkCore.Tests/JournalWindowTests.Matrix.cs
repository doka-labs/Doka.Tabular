using System.Linq;
using Doka.Tabular.Csv;
using Doka.Tabular.Excel;
using JetBrains.Annotations;
using Xunit;

namespace Doka.Tabular.EntityFrameworkCore.Tests;

/// <summary>
/// The import log follows every outcome: a stored start ends once with one completion and a reference in the report,
/// and the claim says what the data shows, which the next import of the same file obeys.
/// </summary>
public sealed partial class JournalWindowTests
{
    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public enum JournalEvent
    {
        None,
        DataErrorInOneRow,
        DataErrorInEveryRow,
        Technical,
        Cancel,
        CommitRejected,
        CommitThrowsAfterwards,
        TechnicalErrorAndRollbackFails,
        InputBreaksWhileHashed,
    }

    public static TheoryData<bool, ImportStrategy, JournalEvent> JournalScenarios()
    {
        var data = new TheoryData<bool, ImportStrategy, JournalEvent>();
        foreach (var workbook in new[] { false, true })
        {
            foreach (var strategy in new[] { ImportStrategy.Strict, ImportStrategy.Partial })
            {
                foreach (var journalEvent in Enum.GetValues<JournalEvent>())
                {
                    data.Add(workbook, strategy, journalEvent);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(JournalScenarios))]
    public async Task ImportLog_FollowsEveryOutcome(
        bool workbook,
        ImportStrategy strategy,
        JournalEvent journalEvent
    )
    {
        // Arrange
        var faults = new TransactionFaults(journalEvent switch
        {
            JournalEvent.CommitRejected => Failure.CommitRejected,
            JournalEvent.CommitThrowsAfterwards => Failure.CommitThrowsAfterwards,
            JournalEvent.TechnicalErrorAndRollbackFails => Failure.RollbackFails,
            _ => Failure.None,
        });

        await using var database = await FileDatabase.CreateAsync(faults);
        var journal = TabularJournal.ForFactory(database.Plain);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var completions = 0;
        var bytes = workbook
            ? (await WorkbookAsync([new UserDto(1, "u1@example.org", "U1"), new UserDto(2, "u2@example.org", "U2")],
                [new UserDto(3, "u3@example.org", "U3")]))
            .ToArray()
            : ((MemoryStream)TestMaps.Csv([.. TestMaps.Rows(3)])).ToArray();

        // Act
        // WHY: The retry of the same file is part of the checked scenario; the state after the first attempt is read
        // before the retry changes it.
        var first = await RunImportAsync(armed: true, Input(journalEvent), cancellation.Token);
        faults.Armed = false;
        await using var check = await database.PlainContextAsync();
        var stored = await check.Users.CountAsync(Token);
        var logs = await journal
            .QueryImports(check)
            .ToListAsync(Token);

        var claims = await journal
            .QueryImportClaims(check)
            .ToListAsync(Token);

        var repeated = await RunImportAsync(armed: false, new MemoryStream(bytes), Token);

        // Assert
        var uncertain = first.RowsUncertain > 0
            || first.FatalError?.Code == TabularErrorCodes.Persistence.CommitUnknown
            || first.FatalError?.Code == TabularErrorCodes.Persistence.RollbackUnknown;

        var label =
            $"workbook={workbook}/{strategy}/{journalEvent}: status={first.Status} imported={first.RowsImported} "
            + $"uncertain={first.RowsUncertain} stored={stored} fatal={first.FatalError?.Code.Value} "
            + $"claims={string.Join(",", claims.Select(claim => claim.State))} completions={completions}";

        // WHY: A scenario with a transaction failure proves only something when the failure was injected.
        Assert.True(journalEvent is not (JournalEvent.CommitRejected
                or JournalEvent.CommitThrowsAfterwards
                or JournalEvent.TechnicalErrorAndRollbackFails)
            || faults.Injected > 0,
            label + " injected=0");

        var log = Assert.Single(logs);
        Assert.True(completions == 1 && first.Journal?.EntryId == log.Id, label);
        Assert.True(log.Status is null == uncertain, label + $" logStatus={log.Status}");

        // WHY: The evidence is committed with the data; stored data proves the import also after an unknown commit.
        ImportClaimState? expected = journalEvent == JournalEvent.InputBreaksWhileHashed
            ? null
            : stored > 0 || (!uncertain && first.Status == ReportStatus.Succeeded)
                ? ImportClaimState.Imported
                : uncertain
                    ? ImportClaimState.Unresolved
                    : ImportClaimState.Released;

        Assert.True(claims.SingleOrDefault()
                ?.State
            == expected,
            label);

        var decision = expected switch
        {
            ImportClaimState.Imported => ImportJournalDecision.Skipped,
            ImportClaimState.Unresolved => ImportJournalDecision.Running,
            _ => ImportJournalDecision.Processed,
        };

        Assert.True(repeated.Journal?.Decision == decision, label + $" repeated={repeated.Journal?.Decision}");

        return;

        Stream Input(
            JournalEvent kind
        ) => kind == JournalEvent.InputBreaksWhileHashed
            ? new FailingSecondReadStream(bytes[..(bytes.Length / 2)], () => new IOException("The input broke off."))
            : new MemoryStream(bytes);

        TabularWriteTarget<UserDto> Target(
            TabularDatabase<TabularTestContext> data,
            bool armed,
            bool logged
        )
        {
            var target = data.PrepareUpsert<UserDto, User>(set: users => users.Users,
                match: TestMaps.UserMatch,
                create: dto =>
                {
                    if (armed && (dto.PersonnelNumber == 2 || journalEvent == JournalEvent.DataErrorInEveryRow))
                    {
                        faults.Armed = true;
                        switch (journalEvent)
                        {
                            case JournalEvent.DataErrorInOneRow or JournalEvent.DataErrorInEveryRow:
                                throw new InvalidUserException("The row is invalid.");
                            case JournalEvent.Technical or JournalEvent.TechnicalErrorAndRollbackFails:
                                throw new IOException("The row failed technically.");
                            case JournalEvent.Cancel:
                                // ReSharper disable once AccessToDisposedClosure
                                // WHY: The callback runs during the awaited import, before the source is disposed.
                                cancellation.Cancel();
                                break;
                        }
                    }

                    return User.Create(dto.PersonnelNumber, dto.Email, dto.Name);
                },
                update: (_, _) => { },
                errors: errors => errors.Expect<InvalidUserException>(TestMaps.UserInvalid));

            return !logged
                ? target
                : target.WithImportLog(journal,
                    new TabularImportLogOptions
                    {
                        Scope = s_scope,
                        OnCompletedAsync = (_, _) =>
                        {
                            if (armed)
                            {
                                completions++;
                            }

                            return Task.CompletedTask;
                        },
                    });
        }

        async Task<(ReportStatus Status, long RowsImported, long RowsUncertain, TabularError? FatalError,
            ImportJournalResult? Journal)> RunImportAsync(
            bool armed,
            Stream input,
            CancellationToken cancellationToken
        )
        {
            // WHY: One database description for all targets of the call; the second sheet writes without a log.
            // ReSharper disable once AccessToDisposedClosure
            var data = TabularDatabase.ForFactory(database.Hooked);
            var target = Target(data, armed, logged: true);
            if (!workbook)
            {
                var report = await s_csv.ImportAsync(input,
                    TestMaps.Users,
                    target,
                    new CsvImportOptions { Strategy = strategy },
                    cancellationToken);

                return (report.Status, report.RowsImported, report.RowsUncertain, report.FatalError, report.Journal);
            }

            var sheets = ExcelImport.Create();
            sheets.Add(TabularSheetName.Create("First"), TestMaps.Users, target);
            sheets.Add(TabularSheetName.Create("Second"), TestMaps.Users, Target(data, armed, logged: false));
            var result = await new ExcelFormat(new ExcelFormatOptions()).ImportAsync(input,
                sheets.Build(),
                new ExcelWorkbookImportOptions { Strategy = strategy },
                cancellationToken);

            return (result.Status, result.RowsImported, result.RowsUncertain, result.FatalError, result.Journal);
        }
    }
}
