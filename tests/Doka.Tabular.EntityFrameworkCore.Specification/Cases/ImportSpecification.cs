namespace Doka.Tabular.EntityFrameworkCore.Specification;

/// <summary>Import into EF targets: strategies, commit groups, database errors, context and transaction rules.</summary>
public abstract partial class ImportSpecification
{
    private static readonly CsvFormat s_csv = new(new CsvFormatOptions());

    private readonly ITestDatabaseProvider _provider;

    protected ImportSpecification(
        ITestDatabaseProvider provider
    )
    {
        _provider = provider;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static CsvImportOptions Partial => new() { Strategy = ImportStrategy.Partial };

    [Fact]
    public async Task Strict_ValidFile_StoresAllRowsInOneTransaction()
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);

        // Act
        var report = await s_csv.ImportAsync(TestMaps.Csv([.. TestMaps.Rows(3)]),
            TestMaps.Users,
            TestMaps.Upsert(TabularDatabase.ForFactory(database.Factory)),
            Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
        Assert.Equal(3, report.RowsImported);
        Assert.Null(report.Items);
        Assert.Equal([1, 2, 3], (await database.UsersAsync()).Select(user => user.PersonnelNumber));
    }

    [Fact]
    public async Task Strict_TwoIndependentDatabaseErrors_ReportsBothAndStoresNothing()
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        var lines = TestMaps
            .Rows(4)
            .ToList();

        lines[1] = "2,user1@example.org,User 2";
        lines[3] = "4,user3@example.org,User 4";

        // Act
        var report = await s_csv.ImportAsync(TestMaps.Csv([.. lines]),
            TestMaps.Users,
            TestMaps.Upsert(TabularDatabase.ForFactory(database.Factory)),
            Token);

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
        Assert.Equal(0, report.RowsImported);
        Assert.Equal([2L, 4L], report.Errors.Select(error => error.Location.RecordNumber!.Value));
        Assert.All(report.Errors, error => Assert.Equal(TestMaps.DuplicateEmail, error.Code));
        Assert.Empty(await database.UsersAsync());
    }

    [Fact]
    public async Task Partial_AssignedDatabaseError_RejectsRowAndStoresTheNext()
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        var lines = TestMaps
            .Rows(3)
            .ToList();

        lines[1] = "2,user1@example.org,User 2";

        // Act
        var report = await s_csv.ImportAsync(TestMaps.Csv([.. lines]),
            TestMaps.Users,
            TestMaps.Upsert(TabularDatabase.ForFactory(database.Factory)),
            Partial,
            Token);

        // Assert
        Assert.Equal(ReportStatus.CompletedWithErrors, report.Status);
        Assert.Equal(2, report.RowsImported);
        Assert.Equal(1, report.RowsRejected);
        var error = Assert.Single(report.Errors);
        Assert.Equal(TestMaps.DuplicateEmail, error.Code);
        Assert.Equal(TabularErrorKind.Data, error.Kind);
        Assert.Equal([1, 3], (await database.UsersAsync()).Select(user => user.PersonnelNumber));
    }

    [Fact]
    public async Task Partial_UnassignedDatabaseError_EndsTheRunAndKeepsConfirmedGroups()
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        var lines = TestMaps
            .Rows(5)
            .ToList();

        lines[3] = "4,user1@example.org,User 4";
        var target = TestMaps
            .Upsert(TabularDatabase.ForFactory(database.Factory), mapDatabaseErrors: false)
            .WithOptions(new TabularWriteOptions { CommitGroupSize = 2 });

        // Act
        var report = await s_csv.ImportAsync(TestMaps.Csv([.. lines]), TestMaps.Users, target, Partial, Token);

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
        Assert.Equal(TabularErrorCodes.Persistence.WriteFailed, report.FatalError?.Code);
        Assert.Equal(2, report.RowsImported);
        Assert.Equal([1, 2], (await database.UsersAsync()).Select(user => user.PersonnelNumber));
    }

    [Theory]
    [InlineData(100, 99, 0)]
    [InlineData(50, 99, 50)]
    public async Task Partial_ParserAbortWithoutKey_KeepsOnlyConfirmedGroups(
        int groupSize,
        int validRows,
        int expected
    )
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        var lines = TestMaps
            .Rows(validRows)
            .Append("100,\"broken,User")
            .ToArray();

        var target = TabularDatabase
            .ForFactory(database.Factory)
            .PrepareInsert<UserDto, User>(set: context => context.Users,
                create: dto => User.Create(dto.PersonnelNumber, dto.Email, dto.Name))
            .WithOptions(new TabularWriteOptions { CommitGroupSize = groupSize });

        // Act
        var report = await s_csv.ImportAsync(TestMaps.Csv(lines), TestMaps.Users, target, Partial, Token);

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
        Assert.Equal(TabularErrorCodes.Parsing.InvalidSyntax, report.FatalError?.Code);
        Assert.Equal(expected, report.RowsImported);
        Assert.Equal(expected, (await database.UsersAsync()).Count);
    }

    [Fact]
    public async Task Partial_CancellationBeforeLastCommit_KeepsConfirmedGroups()
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var target = TabularDatabase
            .ForFactory(database.Factory)
            .PrepareInsert<UserDto, User>(
                set: context => context.Users,
                create: dto =>
                {
                    if (dto.PersonnelNumber == 5)
                    {
                        // ReSharper disable once AccessToDisposedClosure
                        // WHY: The callback runs during the awaited import, before the captured object is disposed.
                        cancellation.Cancel();
                    }

                    return User.Create(dto.PersonnelNumber, dto.Email, dto.Name);
                })
            .WithOptions(new TabularWriteOptions { CommitGroupSize = 2 });

        // Act
        var report = await s_csv.ImportAsync(TestMaps.Csv([.. TestMaps.Rows(5)]),
            TestMaps.Users,
            target,
            Partial,
            cancellation.Token);

        // Assert
        Assert.Equal(ReportStatus.Canceled, report.Status);
        Assert.Equal(4, report.RowsImported);
        Assert.Equal(4, (await database.UsersAsync()).Count);
    }

    [Fact]
    public async Task InsertWithMatch_ExistingRecord_IsSkippedWithoutError()
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        await database.SeedAsync(User.Create(2, "old@example.org", "Old"));
        var target = TabularDatabase
            .ForFactory(database.Factory)
            .PrepareInsert<UserDto, User>(
                set: context => context.Users,
                create: dto => User.Create(dto.PersonnelNumber, dto.Email, dto.Name),
                match: TestMaps.UserMatch);

        // Act
        var report = await s_csv.ImportAsync(TestMaps.Csv([.. TestMaps.Rows(3)]), TestMaps.Users, target, Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
        Assert.Equal(2, report.RowsImported);
        Assert.Equal(1, report.RowsSkipped);
        Assert.Equal("old@example.org", (await database.UsersAsync())[1].Email);
    }

    [Fact]
    public async Task Upsert_ExistingRecord_RunsOnlyUpdateAndKeepsTheEntityId()
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        var existing = User.Create(42, "old@example.org", "Old");
        await database.SeedAsync(existing);
        var created = 0;
        var target = TabularDatabase
            .ForFactory(database.Factory)
            .PrepareUpsert<UserDto, User>(
                set: context => context.Users,
                match: TestMaps.UserMatch,
                create: dto =>
                {
                    created++;

                    return User.Create(dto.PersonnelNumber, dto.Email, dto.Name);
                },
                update: (entity, dto) => entity.SetEmail(dto.Email));

        // Act
        var report = await s_csv.ImportAsync(TestMaps.Csv("42,new@example.org,New"), TestMaps.Users, target, Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
        Assert.Equal(0, created);
        var user = Assert.Single(await database.UsersAsync());
        Assert.Equal((existing.Id, "new@example.org", "Old"), (user.Id, user.Email, user.Name));
    }

    [Fact]
    public async Task Update_AssignedExceptionAfterFirstChange_LeavesTheRecordUnchanged()
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        await database.SeedAsync(User.Create(1, "old@example.org", "Old"));
        var name = new string('x', 41);

        // Act
        var report = await s_csv.ImportAsync(TestMaps.Csv($"1,new@example.org,{name}", "2,user2@example.org,User 2"),
            TestMaps.Users,
            TestMaps.Upsert(TabularDatabase.ForFactory(database.Factory)),
            Partial,
            Token);

        // Assert
        Assert.Equal(ReportStatus.CompletedWithErrors, report.Status);
        Assert.Equal(TestMaps.UserInvalid,
            Assert.Single(report.Errors)
                .Code);

        var users = await database.UsersAsync();
        Assert.Equal("old@example.org", users[0].Email);
        Assert.Equal(2, users.Count);
    }

    [Fact]
    public async Task Create_UnassignedException_EndsTheRunAsTechnicalError()
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        var target = TabularDatabase
            .ForFactory(database.Factory)
            .PrepareInsert<UserDto, User>(
                set: context => context.Users,
                create: _ => throw new FormatException("unexpected"));

        // Act
        var report =
            await s_csv.ImportAsync(TestMaps.Csv([.. TestMaps.Rows(2)]), TestMaps.Users, target, Partial, Token);

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
        Assert.Equal(TabularErrorCodes.Callback.Failed, report.FatalError?.Code);
        Assert.DoesNotContain("unexpected", report.FatalError!.Message, StringComparison.Ordinal);
        Assert.Empty(await database.UsersAsync());
    }

    [Fact]
    public async Task SourceKey_RepeatedInFile_RejectsAllOccurrences()
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);

        // Act
        var report = await s_csv.ImportAsync(
            TestMaps.Csv("7,a@example.org,A", "8,b@example.org,B", "7,c@example.org,C"),
            TestMaps.Users,
            TestMaps.Upsert(TabularDatabase.ForFactory(database.Factory)),
            Partial,
            Token);

        // Assert
        Assert.Equal(ReportStatus.CompletedWithErrors, report.Status);
        Assert.Equal([1L, 3L], report.Errors.Select(error => error.Location.RecordNumber!.Value));
        Assert.All(report.Errors, error => Assert.Equal(TabularErrorCodes.Key.Duplicate, error.Code));
        Assert.Equal([8], (await database.UsersAsync()).Select(user => user.PersonnelNumber));
    }

    [Fact]
    public async Task ReturnItems_ContainsOnlyCommittedRows()
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        var lines = TestMaps
            .Rows(3)
            .ToList();

        lines[2] = "3,user1@example.org,User 3";
        var target = TestMaps
            .Upsert(TabularDatabase.ForFactory(database.Factory))
            .WithOptions(new TabularWriteOptions { ReturnItems = true });

        // Act
        var report = await s_csv.ImportAsync(TestMaps.Csv([.. lines]), TestMaps.Users, target, Partial, Token);

        // Assert
        Assert.Equal([1, 2], report.Items!.Select(item => item.PersonnelNumber));
    }
}
