using Doka.Tabular.Testing;

namespace Doka.Tabular.EntityFrameworkCore.Specification;

/// <summary>
/// Concurrent inserts of the same keys store each record once on every engine; a real conflict of two inserts is a row
/// error of the database mapping, not a failure of the run.
/// </summary>
public abstract partial class ImportSpecification
{
    private static readonly TabularErrorCode s_duplicate = TabularErrorCode.Create("user.duplicate");

    [Fact]
    public async Task ConcurrentInsertsOfTheSameKeys_StoreEachRecordOnce()
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        const int rows = 40;

        // Act
        var reports = await Task.WhenAll(
            InsertUsersAsync(database, rows, _ => { }),
            InsertUsersAsync(database, rows, _ => { }));

        // Assert
        Assert.Equal(rows, reports.Sum(report => report.RowsImported));
        Assert.Equal(2 * rows, reports.Sum(report => report.RowsImported + report.RowsSkipped + report.RowsRejected));
        Assert.All(reports.SelectMany(report => report.Errors), error => Assert.Equal(s_duplicate, error.Code));
        Assert.Equal(Enumerable.Range(1, rows), (await database.UsersAsync()).Select(user => user.PersonnelNumber));
    }

    [Fact]
    public async Task TwoInsertsOfTheSameKeyAfterBothFoundNoMatch_AreOneStoredRecordAndOneRowError()
    {
        // Arrange
        Assert.SkipUnless(
            _provider.SupportsConcurrentWriters,
            "The engine serializes writers; two inserts of one key at the same time need a server database.");

        await using var database = await _provider.CreateAsync(Token);
        const int rows = 5;

        // WHY: Both imports meet in the creation of the first record, after both found no match for it, so both insert
        // it and the database has to reject one of them; without the meeting the second import would only skip. The
        // meeting has no deadline, and an import that fails before it leaves it.
        using var meeting = new Meeting(2);

        // Act
        var reports = await Task.WhenAll(meeting.JoinAsync(Insert, Token), meeting.JoinAsync(Insert, Token));

        // Assert
        // WHY: After the meeting both imports run in step, so later records can collide as well; each collision is a
        // rejected row, and every record is stored once.
        var errors = reports
            .SelectMany(report => report.Errors)
            .ToList();

        Assert.Equal(2, meeting.Met);
        Assert.NotEmpty(errors);
        Assert.All(errors, error => Assert.Equal((s_duplicate, TabularErrorKind.Data), (error.Code, error.Kind)));
        Assert.All(reports, report => Assert.Null(report.FatalError));
        Assert.Equal(rows, reports.Sum(report => report.RowsImported));
        Assert.Equal(2 * rows, reports.Sum(report => report.RowsImported + report.RowsSkipped + report.RowsRejected));
        Assert.Equal(Enumerable.Range(1, rows), (await database.UsersAsync()).Select(user => user.PersonnelNumber));

        return;

        // ReSharper disable once AccessToDisposedClosure
        // WHY: Both imports are awaited in the act, before the captured database is disposed.
        Task<ImportReport<UserDto>> Insert() => InsertUsersAsync(database, rows, Meet);

        void Meet(
            UserDto dto
        )
        {
            if (dto.PersonnelNumber == 1)
            {
                // ReSharper disable once AccessToDisposedClosure
                // WHY: The callback runs during the awaited imports, before the captured meeting is disposed.
                meeting.Arrive(Token);
            }
        }
    }

    /// <summary>Inserts the users with one commit group per row; the hook runs before each record is created.</summary>
    private static Task<ImportReport<UserDto>> InsertUsersAsync(
        TestDatabase database,
        int rows,
        Action<UserDto> beforeCreate
    ) => s_csv.ImportAsync(
        TestMaps.Csv([.. TestMaps.Rows(rows)]),
        TestMaps.Users,
        TabularDatabase
            .ForFactory(database.Factory)
            .PrepareInsert<UserDto, User>(
                set: context => context.Users,
                create: dto =>
                {
                    beforeCreate(dto);

                    return User.Create(dto.PersonnelNumber, dto.Email, dto.Name);
                },
                match: TestMaps.UserMatch,
                errors: errors => errors.Database(_ => s_duplicate))
            .WithOptions(new TabularWriteOptions { CommitGroupSize = 1 }),
        Partial,
        Token);
}
