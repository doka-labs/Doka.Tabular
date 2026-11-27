using System.Data.Common;
using System.Linq;
using Doka.Tabular.Csv;
using Doka.Tabular.Excel;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Doka.Tabular.EntityFrameworkCore.Tests;

/// <summary>The reports of a partial workbook import keep what the writing phase did to every sheet.</summary>
public sealed class WorkbookWriteTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task JoinedPartialWorkbook_LaterFatalSheet_LeavesNoStalePendingRows()
    {
        // Arrange
        await using var sqlite = await OpenAsync();
        await using var context = new TabularTestContext(Options(sqlite));
        await using var transaction = await context.Database.BeginTransactionAsync(Token);
        var target = Target(TabularDatabase.ForContext(context), _ => throw new FormatException())
            .WithOptions(new TabularWriteOptions { JoinExistingTransaction = true });

        // Act
        var (report, first, second) = await ImportAsync(target);
        await transaction.CommitAsync(Token);

        // Assert
        await using var check = new TabularTestContext(Options(sqlite));
        Assert.Equal(
            (ReportStatus.Failed, 0L, 0),
            (report.Status, report.RowsPendingCommit,
                await check.Users.CountAsync(Token)));

        Assert.Equal(
            (ReportStatus.Failed, false, 0L),
            (report.Get(first)
                .Status, report.Get(first)
                .IsComplete, report.Get(first)
                .RowsPendingCommit));

        Assert.Equal(
            ReportStatus.Failed,
            report.Get(second)
                .Status);
    }

    [Fact]
    public async Task PartialWorkbook_CancellationWhileWritingTheLastSheet_IsCanceled()
    {
        // Arrange
        await using var sqlite = await OpenAsync();
        await using var context = new TabularTestContext(Options(sqlite));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var target = Target(
            TabularDatabase.ForContext(context),
            dto =>
            {
                // ReSharper disable once AccessToDisposedClosure
                // WHY: The callback runs during the awaited import, before the captured object is disposed.
                cancellation.Cancel();

                return User.Create(dto.PersonnelNumber, dto.Email, dto.Name);
            });

        // Act
        var (report, first, second) = await ImportAsync(target, cancellation.Token);

        // Assert
        Assert.Equal((ReportStatus.Canceled, false), (report.Status, report.IsComplete));
        Assert.Equal(
            (ReportStatus.Succeeded, 1L),
            (report.Get(first)
                .Status, report.Get(first)
                .RowsImported));

        Assert.Equal(
            (ReportStatus.Canceled, false),
            (report.Get(second)
                .Status, report.Get(second)
                .IsComplete));
    }

    [Fact]
    public async Task PartialWorkbook_TechnicalWriteFailureOfASheet_FailsThatSheet()
    {
        // Arrange
        await using var sqlite = await OpenAsync();
        await using var context = new TabularTestContext(Options(sqlite));
        var target = Target(TabularDatabase.ForContext(context), _ => throw new FormatException());

        // Act
        var (report, first, second) = await ImportAsync(target);

        // Assert
        Assert.Equal(
            (ReportStatus.Failed, TabularErrorCodes.Callback.Failed),
            (report.Status, report.FatalError?.Code));

        Assert.Equal(
            ReportStatus.Succeeded,
            report.Get(first)
                .Status);

        Assert.Equal(
            (ReportStatus.Failed, false),
            (report.Get(second)
                .Status, report.Get(second)
                .IsComplete));
    }

    [Fact]
    public async Task PartialSheet_TechnicalFailureAfterAnEarlierGroupCommit_FailsTheSheet()
    {
        // Arrange
        await using var sqlite = await OpenAsync();
        await using var context = new TabularTestContext(Options(sqlite));
        var target = Target(TabularDatabase.ForContext(context), _ => throw new FormatException())
            .WithOptions(new TabularWriteOptions { CommitGroupSize = 1 });

        var excel = new ExcelFormat(new ExcelFormatOptions());
        var export = ExcelExport.Create();
        export.Add(
            TabularSheetName.Create("First"),
            [new UserDto(1, "a@example.org", "Ada"), new UserDto(2, "b@example.org", "Bob")],
            TestMaps.Users);

        var file = new MemoryStream();
        await excel.ExportAsync(export.Build(), file, Token);
        file.Position = 0;
        var sheets = ExcelImport.Create();
        var first = sheets.Add(TabularSheetName.Create("First"), TestMaps.Users, target);

        // Act
        var report = await excel.ImportAsync(
            file,
            sheets.Build(),
            new ExcelWorkbookImportOptions { Strategy = ImportStrategy.Partial },
            Token);

        // Assert
        await using var check = new TabularTestContext(Options(sqlite));
        Assert.Equal(
            (ReportStatus.Failed, false, 1L),
            (report.Get(first)
                .Status, report.Get(first)
                .IsComplete, report.Get(first)
                .RowsImported));

        Assert.Equal(1, await check.Users.CountAsync(Token));
    }

    [Fact]
    public async Task JoinedPartialWorkbook_CancellationWhileReadingALaterSheet_RollsBackTheImport()
    {
        // Arrange
        await using var sqlite = await OpenAsync();
        await using var context = new TabularTestContext(Options(sqlite));
        await using var transaction = await context.Database.BeginTransactionAsync(Token);
        context.Entries.Add(new ProcessingEntry { State = "started" });
        await context.SaveChangesAsync(Token);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var target = Target(
                TabularDatabase.ForContext(context),
                dto => User.Create(dto.PersonnelNumber, dto.Email, dto.Name))
            .WithOptions(new TabularWriteOptions { JoinExistingTransaction = true });

        var canceling = TabularMap
            .For<UserDto>()
            .Column(
                x => x.PersonnelNumber,
                index: 0,
                configure: c => c.Validate(number =>
                {
                    // ReSharper disable once AccessToDisposedClosure
                    // WHY: The rule runs during the awaited import, before the captured object is disposed.
                    cancellation.Cancel();

                    return number > 0;
                }))
            .Column(x => x.Email, index: 1)
            .Column(x => x.Name, index: 2)
            .Build();

        // Act
        var (report, first, _) = await ImportAsync(target, cancellation.Token, canceling);
        await transaction.CommitAsync(Token);

        // Assert
        await using var check = new TabularTestContext(Options(sqlite));
        Assert.Equal(
            (ReportStatus.Canceled, 0L, 0L),
            (report.Status, report.RowsPendingCommit, report.Get(first)
                .RowsPendingCommit));

        Assert.Equal((0, 1), (await check.Users.CountAsync(Token), await check.Entries.CountAsync(Token)));
    }

    [Fact]
    public async Task JoinedPartialWorkbook_ThrownAbortWhileReadingALaterSheet_RollsBackTheImport()
    {
        // Arrange
        await using var sqlite = await OpenAsync();
        await using var context = new TabularTestContext(Options(sqlite));
        await using var transaction = await context.Database.BeginTransactionAsync(Token);
        var target = Target(
                TabularDatabase.ForContext(context),
                dto => User.Create(dto.PersonnelNumber, dto.Email, dto.Name))
            .WithOptions(new TabularWriteOptions { JoinExistingTransaction = true });

        // Act
        // WHY: The long name of the second sheet exceeds the field limit while the sheet is read.
        var (report, first, _) = await ImportAsync(
            target,
            secondName: new string('b', 40),
            options: new ExcelFormatOptions { MaxFieldCharacters = 20 });

        await transaction.CommitAsync(Token);

        // Assert
        await using var check = new TabularTestContext(Options(sqlite));
        Assert.Equal(
            (TabularErrorCodes.Resource.LimitExceeded, 0L, ReportStatus.Failed),
            (report.FatalError?.Code, report.RowsPendingCommit, report.Get(first)
                .Status));

        Assert.Equal(0, await check.Users.CountAsync(Token));
    }

    [Fact]
    public async Task JoinedPartialWorkbook_FailedRollbackOfTheImport_LeavesTheRowsUncertain()
    {
        // Arrange
        await using var sqlite = await OpenAsync();
        var failing = new FailSavepointRollback();
        await using var context = new TabularTestContext(
            new DbContextOptionsBuilder<TabularTestContext>()
                .UseSqlite(sqlite.Connection)
                .AddInterceptors(failing)
                .Options);

        await using var transaction = await context.Database.BeginTransactionAsync(Token);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var target = Target(
                TabularDatabase.ForContext(context),
                dto => User.Create(dto.PersonnelNumber, dto.Email, dto.Name))
            .WithOptions(new TabularWriteOptions { JoinExistingTransaction = true });

        // Act
        var (report, first, _) = await ImportAsync(
            target,
            cancellation.Token,
            CancelingMap(() =>
            {
                failing.Armed = true;

                // ReSharper disable once AccessToDisposedClosure
                // WHY: The rule runs during the awaited import, before the captured object is disposed.
                cancellation.Cancel();
            }));

        // Assert
        Assert.Equal(
            (0L, 1L, ReportStatus.Failed),
            (report.RowsPendingCommit, report.RowsUncertain, report.Get(first)
                .Status));

        Assert.Equal(TabularErrorCodes.Persistence.RollbackUnknown, report.FatalError?.Code);
    }

    [Fact]
    public async Task CancellationOfTheCallInCreate_IsACanceledReport()
    {
        // Arrange
        await using var sqlite = await OpenAsync();
        await using var context = new TabularTestContext(Options(sqlite));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var target = Target(
            TabularDatabase.ForContext(context),
            _ =>
            {
                // ReSharper disable once AccessToDisposedClosure
                // WHY: The callback runs during the awaited import, before the captured object is disposed.
                cancellation.Cancel();

                throw new OperationCanceledException();
            });

        // Act
        var report = await new CsvFormat(new CsvFormatOptions()).ImportAsync(
            TestMaps.Csv("1,a@example.org,Ada", "2,b@example.org,Bob"),
            TestMaps.Users,
            target,
            new CsvImportOptions { Strategy = ImportStrategy.Partial },
            cancellation.Token);

        // Assert
        Assert.Equal((ReportStatus.Canceled, null), (report.Status, report.FatalError));
    }

    [Fact]
    public async Task SeveralSourceKeys_AreCheckedIndependently()
    {
        // Arrange
        await using var sqlite = await OpenAsync();
        await using var context = new TabularTestContext(Options(sqlite));

        // Act
        // WHY: The email key rejects rows 1 and 2; the personnel number of row 3 still repeats that of row 2.
        var report = await new CsvFormat(new CsvFormatOptions()).ImportAsync(
            TestMaps.Csv("10,a@example.org,Ada", "20,a@example.org,Bob", "20,c@example.org,Cy"),
            TestMaps.Users,
            TestMaps.Upsert(TabularDatabase.ForContext(context)),
            new CsvImportOptions
            {
                Strategy = ImportStrategy.Partial,
                SourceKey = TestMaps.Users.Key(x => x.Email),
            },
            Token);

        // Assert
        Assert.Equal((0L, 3L), (report.RowsImported, report.RowsRejected));
        Assert.Equal(0, await context.Users.CountAsync(Token));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StopOfASecondSourceKey_KeepsTheRejectionsOfTheFirst(
        bool cancel
    )
    {
        // Arrange
        await using var sqlite = await OpenAsync();
        await using var context = new TabularTestContext(Options(sqlite));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var match = TabularMatch
            .For<UserDto, User>(TestMaps.Users)
            .Part(
                dto => dto.PersonnelNumber,
                entity => entity.PersonnelNumber,
                new StoppingComparer(cancellation, cancel))
            .UseUniqueKey()
            .Build();

        var target = TabularDatabase
            .ForContext(context)
            .PrepareUpsert<UserDto, User>(
                set: users => users.Users,
                match: match,
                create: dto => User.Create(dto.PersonnelNumber, dto.Email, dto.Name),
                update: (_, _) => { });

        // Act
        // WHY: The email key rejects rows 1 and 2 before the comparer of the second key stops the check.
        var report = await new CsvFormat(new CsvFormatOptions()).ImportAsync(
            TestMaps.Csv("10,a@example.org,Ada", "20,a@example.org,Bob", "30,c@example.org,Cy"),
            TestMaps.Users,
            target,
            new CsvImportOptions
            {
                Strategy = ImportStrategy.Partial,
                SourceKey = TestMaps.Users.Key(x => x.Email),
            },
            cancellation.Token);

        // Assert
        Assert.Equal((2L, 0L), (report.RowsRejected, report.RowsImported));
        Assert.Equal(
            cancel
                ? ReportStatus.Canceled
                : ReportStatus.Failed,
            report.Status);
    }

    [Fact]
    public async Task JoinedImport_FailedRollbackOfTheOpenRows_LeavesThemUncertain()
    {
        // Arrange
        await using var sqlite = await OpenAsync();
        var failing = new FailSavepointRollback();
        await using var context = new TabularTestContext(
            new DbContextOptionsBuilder<TabularTestContext>()
                .UseSqlite(sqlite.Connection)
                .AddInterceptors(failing)
                .Options);

        await using var transaction = await context.Database.BeginTransactionAsync(Token);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var target = Target(
                TabularDatabase.ForContext(context),
                dto =>
                {
                    failing.Armed = true;

                    // ReSharper disable once AccessToDisposedClosure
                    // WHY: The callback runs during the awaited import, before the captured object is disposed.
                    cancellation.Cancel();

                    return User.Create(dto.PersonnelNumber, dto.Email, dto.Name);
                })
            .WithOptions(new TabularWriteOptions { JoinExistingTransaction = true });

        // Act
        var report = await new CsvFormat(new CsvFormatOptions()).ImportAsync(
            TestMaps.Csv("1,a@example.org,Ada", "2,b@example.org,Bob"),
            TestMaps.Users,
            target,
            new CsvImportOptions { Strategy = ImportStrategy.Partial },
            cancellation.Token);

        // Assert
        Assert.Equal(TabularErrorCodes.Persistence.RollbackUnknown, report.FatalError?.Code);

        // WHY: Row 1 was saved in the open import savepoint; the reset of the canceled row 2 failed as well, so
        // neither row is known to be rolled back.
        Assert.Equal((0L, 0L, 2L), (report.RowsImported, report.RowsPendingCommit, report.RowsUncertain));
    }

    [Fact]
    public async Task JoinedWorkbook_FailedRollbackOfALaterSheet_LeavesThePendingRowsOfTheEarlierOneUncertain()
    {
        // Arrange
        await using var sqlite = await OpenAsync();
        var failing = new FailSavepointRollback();
        await using var context = new TabularTestContext(
            new DbContextOptionsBuilder<TabularTestContext>()
                .UseSqlite(sqlite.Connection)
                .AddInterceptors(failing)
                .Options);

        await using var transaction = await context.Database.BeginTransactionAsync(Token);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var target = Target(
                TabularDatabase.ForContext(context),
                dto =>
                {
                    failing.Armed = true;

                    // ReSharper disable once AccessToDisposedClosure
                    // WHY: The callback runs during the awaited import, before the captured object is disposed.
                    cancellation.Cancel();

                    return User.Create(dto.PersonnelNumber, dto.Email, dto.Name);
                })
            .WithOptions(new TabularWriteOptions { JoinExistingTransaction = true });

        // Act
        var (report, first, second) = await ImportAsync(target, cancellation.Token);

        // Assert
        // WHY: The pending row of the first sheet and the row of the second, whose reset failed, both stay uncertain.
        Assert.Equal((0L, 2L), (report.RowsPendingCommit, report.RowsUncertain));
        Assert.Equal(
            (1L, 1L),
            (report.Get(first)
                .RowsUncertain, report.Get(second)
                .RowsUncertain));
    }

    [Fact]
    public async Task StrictJoinedWorkbook_FailedRollback_LeavesThePendingRowsOfTheEarlierSheetUncertain()
    {
        // Arrange
        await using var sqlite = await OpenAsync();
        var failing = new FailSavepointRollback();
        await using var context = new TabularTestContext(
            new DbContextOptionsBuilder<TabularTestContext>()
                .UseSqlite(sqlite.Connection)
                .AddInterceptors(failing)
                .Options);

        await using var transaction = await context.Database.BeginTransactionAsync(Token);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var target = Target(
                TabularDatabase.ForContext(context),
                dto =>
                {
                    failing.Armed = true;

                    // ReSharper disable once AccessToDisposedClosure
                    // WHY: The callback runs during the awaited import, before the captured object is disposed.
                    cancellation.Cancel();

                    return User.Create(dto.PersonnelNumber, dto.Email, dto.Name);
                })
            .WithOptions(new TabularWriteOptions { JoinExistingTransaction = true });

        var excel = new ExcelFormat(new ExcelFormatOptions());
        var export = ExcelExport.Create();
        export.Add(TabularSheetName.Create("First"), [new UserDto(1, "a@example.org", "Ada")], TestMaps.Users);
        export.Add(TabularSheetName.Create("Second"), [new UserDto(2, "b@example.org", "Bob")], TestMaps.Users);
        var file = new MemoryStream();
        await excel.ExportAsync(export.Build(), file, Token);
        file.Position = 0;
        var sheets = ExcelImport.Create();
        var first = sheets.Add(TabularSheetName.Create("First"), TestMaps.Users, target);
        sheets.Add(TabularSheetName.Create("Second"), TestMaps.Users, target);

        // Act
        var report = await excel.ImportAsync(
            file,
            sheets.Build(),
            new ExcelWorkbookImportOptions { Strategy = ImportStrategy.Strict },
            cancellation.Token);

        // Assert
        Assert.Equal(
            (0L, 1L),
            (report.RowsPendingCommit, report.Get(first)
                .RowsUncertain));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task RowDestinationOfAnotherRowMap_IsAConfigurationErrorBeforeReading(
        int form
    )
    {
        // Arrange
        await using var sqlite = await OpenAsync();
        await using var context = new TabularTestContext(Options(sqlite));
        var addressMap = TabularMap
            .For<AddressDto>()
            .Column(
                x => x.PersonnelNumber,
                index: 0,
                key: ColumnKey.Create("addressPersonnelNumber"),
                configure: c => c.ImportOnly())
            .Column(x => x.City, index: 3)
            .Build();

        var own = RowMap.Create();
        var userRole = own.Add(TestMaps.Users);
        var addressRole = own.Add(addressMap);
        var ownMap = own.Build();
        var swapped = RowMap.Create();
        swapped.Add(addressMap);
        swapped.Add(TestMaps.Users);
        var swappedMap = swapped.Build();
        var database = TabularDatabase.ForContext(context);
        var users = TestMaps.Upsert(database);
        var addresses = database.PrepareUpsert(
            set: c => c.Addresses,
            match: TabularMatch
                .For<AddressDto, Address>(addressMap)
                .Part(
                    dto => dto.PersonnelNumber,
                    address => address.UserId,
                    valueFrom: users.Value(userRole, user => user.Id))
                .UseUniqueKey()
                .Build(),
            create: (_, _) => throw new InvalidOperationException("The import must not reach the target."),
            update: (address, dto) => address.SetCity(dto.City));

        var destination = TabularDestination
            .For(ownMap)
            .Bind(userRole, users)
            .Bind(addressRole, addresses)
            .Build();

        var csv = TestMaps.Csv("1,a@example.org,A,Berlin");
        var excel = new MemoryStream();
        await new ExcelFormat(new ExcelFormatOptions()).ExportAsync(
            new[] { new Line(1, "a@example.org", "A", "Berlin") },
            TabularMap
                .For<Line>()
                .Column(x => x.Number, index: 0)
                .Column(x => x.Email, index: 1)
                .Column(x => x.Name, index: 2)
                .Column(x => x.City, index: 3)
                .Build(),
            excel,
            Token);

        excel.Position = 0;

        // Act
        IReadOnlyList<TabularError> errors;
        switch (form)
        {
            case 0:
                errors = (await new CsvFormat(new CsvFormatOptions()).ImportAsync(csv, swappedMap, destination, Token))
                    .Errors;

                break;
            case 1:
                errors = (await new ExcelFormat(new ExcelFormatOptions()).ImportAsync(
                    excel,
                    swappedMap,
                    destination,
                    Token)).Errors;

                break;
            default:
                var sheets = ExcelImport.Create();
                sheets.Add(TabularSheetName.Create("Sheet1"), swappedMap, destination);
                errors = (await new ExcelFormat(new ExcelFormatOptions()).ImportAsync(
                    excel,
                    sheets.Build(),
                    Token)).Errors;

                break;
        }

        // Assert
        await using var check = new TabularTestContext(Options(sqlite));
        Assert.Equal(
            TabularErrorKind.Configuration,
            Assert.Single(errors)
                .Kind);

        Assert.Equal(0, await check.Users.CountAsync(Token));
    }

    [Theory]
    [InlineData("Fail", "Berlin", "billing.rejected")]
    [InlineData("Berlin", "Fail", "shipping.rejected")]
    public async Task DatabaseErrorOfOneOfTwoTargetsOfTheSameEntityType_UsesTheMappingOfThatTarget(
        string billingCity,
        string shippingCity,
        string expected
    )
    {
        // Arrange
        await using var sqlite = await OpenAsync();
        await using var context = new TabularTestContext(
            new DbContextOptionsBuilder<TabularTestContext>()
                .UseSqlite(sqlite.Connection)
                .AddInterceptors(new RejectCity("Fail"))
                .Options);

        var cityMap = TabularMap
            .For<CityDto>()
            .Column(x => x.City, index: 0)
            .Build();

        var rows = RowMap.Create();
        var userRole = rows.Add(TestMaps.Users);
        var billingRole = rows.Add(
            cityMap,
            e => e
                .StartIndex(3)
                .KeyPrefix(ColumnKey.Create("billing")));

        var shippingRole = rows.Add(
            cityMap,
            e => e
                .StartIndex(4)
                .KeyPrefix(ColumnKey.Create("shipping")));

        var rowMap = rows.Build();
        var database = TabularDatabase.ForContext(context);
        var users = TestMaps.Upsert(database);
        var userId = users.Value(userRole, user => user.Id);
        var destination = TabularDestination
            .For(rowMap)
            .Bind(userRole, users)
            .Bind(billingRole, Addresses("billing.rejected"))
            .Bind(shippingRole, Addresses("shipping.rejected"))
            .Build();

        // Act
        var report = await new CsvFormat(new CsvFormatOptions()).ImportAsync(
            TestMaps.Csv($"1,a@example.org,Ada,{billingCity},{shippingCity}"),
            rowMap,
            destination,
            new CsvImportOptions { Strategy = ImportStrategy.Partial },
            Token);

        // Assert
        Assert.Equal(
            expected,
            Assert.Single(report.Errors)
                .Code.Value);

        return;

        TabularWriteTarget<CityDto> Addresses(
            string code
        ) => database.PrepareInsert<CityDto, Address>(
            set: c => c.Addresses,
            create: (dto, values) => Address.Create(values.Get(userId), dto.City),
            errors: errors => errors.Database(_ => TabularErrorCode.Create(code)));
    }

    [Fact]
    public async Task RowWithSeveralTargets_SavedInPartAndNotReset_IsUncertain()
    {
        // Arrange
        await using var sqlite = await OpenAsync();
        var failing = new FailSavepointRollback();
        var failingTransaction = new FailRollback();
        await using var context = new TabularTestContext(
            new DbContextOptionsBuilder<TabularTestContext>()
                .UseSqlite(sqlite.Connection)
                .AddInterceptors(failing, failingTransaction)
                .Options);

        var addressMap = TabularMap
            .For<AddressDto>()
            .Column(
                x => x.PersonnelNumber,
                index: 0,
                key: ColumnKey.Create("addressPersonnelNumber"),
                configure: c => c.ImportOnly())
            .Column(x => x.City, index: 3)
            .Build();

        var rows = RowMap.Create();
        var userRole = rows.Add(TestMaps.Users);
        var addressRole = rows.Add(addressMap);
        var rowMap = rows.Build();
        var database = TabularDatabase.ForContext(context);
        var users = TestMaps.Upsert(database);

        var userId = users.Value(userRole, user => user.Id);
        var addresses = database
            .PrepareUpsert(
                set: c => c.Addresses,
                match: TabularMatch
                    .For<AddressDto, Address>(addressMap)
                    .Part(dto => dto.PersonnelNumber, address => address.UserId, valueFrom: userId)
                    .UseUniqueKey()
                    .Build(),
                create: (_, _) =>
                {
                    // WHY: The user of the row is saved for its generated identifier; the address then fails.
                    failing.Armed = true;
                    failingTransaction.Armed = true;

                    throw new IOException("The address cannot be created.");
                },
                update: (address, dto) => address.SetCity(dto.City));

        var destination = TabularDestination
            .For(rowMap)
            .Bind(userRole, users)
            .Bind(addressRole, addresses)
            .Build();

        // Act
        var report = await new CsvFormat(new CsvFormatOptions()).ImportAsync(
            TestMaps.Csv("1,a@example.org,A,Berlin"),
            rowMap,
            destination,
            new CsvImportOptions { Strategy = ImportStrategy.Strict },
            Token);

        // Assert
        Assert.Equal((0L, 1L), (report.RowsImported, report.RowsUncertain));
    }

    [Fact]
    public async Task CancellationRightAfterASavedRow_WithAFailedRollback_LeavesTheRowUncertain()
    {
        // Arrange
        await using var sqlite = await OpenAsync();
        var failing = new FailSavepointRollback();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        await using var context = new TabularTestContext(
            new DbContextOptionsBuilder<TabularTestContext>()
                .UseSqlite(sqlite.Connection)
                .AddInterceptors(failing, new CancelAfterTheSave(cancellation, failing))
                .Options);

        await using var transaction = await context.Database.BeginTransactionAsync(Token);
        var target = TestMaps
            .Upsert(TabularDatabase.ForContext(context))
            .WithOptions(new TabularWriteOptions { JoinExistingTransaction = true });

        // Act
        var report = await new CsvFormat(new CsvFormatOptions()).ImportAsync(
            TestMaps.Csv("1,a@example.org,Ada", "2,b@example.org,Bob"),
            TestMaps.Users,
            target,
            new CsvImportOptions { Strategy = ImportStrategy.Partial },
            cancellation.Token);

        // Assert
        Assert.Equal(
            (TabularErrorCodes.Persistence.RollbackUnknown, 0L, 1L),
            (report.FatalError?.Code, report.RowsPendingCommit, report.RowsUncertain));
    }

    [Fact]
    public async Task StrictWorkbook_FailedRollbackOfTheSharedTransaction_LeavesTheEarlierSheetUncertain()
    {
        // Arrange
        await using var sqlite = await OpenAsync();
        var failing = new FailRollback();
        await using var context = new TabularTestContext(
            new DbContextOptionsBuilder<TabularTestContext>()
                .UseSqlite(sqlite.Connection)
                .AddInterceptors(failing)
                .Options);

        var target = Target(
            TabularDatabase.ForContext(context),
            _ =>
            {
                failing.Armed = true;

                throw new IOException("The second row fails technically.");
            });

        var excel = new ExcelFormat(new ExcelFormatOptions());
        var export = ExcelExport.Create();
        export.Add(TabularSheetName.Create("First"), [new UserDto(1, "a@example.org", "Ada")], TestMaps.Users);
        export.Add(TabularSheetName.Create("Second"), [new UserDto(2, "b@example.org", "Bob")], TestMaps.Users);
        var file = new MemoryStream();
        await excel.ExportAsync(export.Build(), file, Token);
        file.Position = 0;
        var sheets = ExcelImport.Create();
        sheets.Add(TabularSheetName.Create("First"), TestMaps.Users, target);
        sheets.Add(TabularSheetName.Create("Second"), TestMaps.Users, target);

        // Act
        var report = await excel.ImportAsync(
            file,
            sheets.Build(),
            new ExcelWorkbookImportOptions { Strategy = ImportStrategy.Strict },
            Token);

        // Assert
        // WHY: The failure of the second row ends the run first; the unconfirmed rollback leaves the first row open.
        Assert.Equal(
            (TabularErrorCodes.Callback.Failed, 0L, 1L),
            (report.FatalError?.Code, report.RowsImported, report.RowsUncertain));
    }

    [Fact]
    public async Task PartialSheet_FailingStartOfTheNextTransactionAfterACommit_FailsTheSheet()
    {
        // Arrange
        await using var sqlite = await OpenAsync();
        await using var context = new TabularTestContext(
            new DbContextOptionsBuilder<TabularTestContext>()
                .UseSqlite(sqlite.Connection)
                .AddInterceptors(new FailStartAfterCommit())
                .Options);

        var target = Target(
                TabularDatabase.ForContext(context),
                dto => User.Create(dto.PersonnelNumber, dto.Email, dto.Name))
            .WithOptions(new TabularWriteOptions { CommitGroupSize = 1 });

        var excel = new ExcelFormat(new ExcelFormatOptions());
        var export = ExcelExport.Create();
        export.Add(
            TabularSheetName.Create("First"),
            [new UserDto(1, "a@example.org", "Ada"), new UserDto(2, "b@example.org", "Bob")],
            TestMaps.Users);

        var file = new MemoryStream();
        await excel.ExportAsync(export.Build(), file, Token);
        file.Position = 0;
        var sheets = ExcelImport.Create();
        var first = sheets.Add(TabularSheetName.Create("First"), TestMaps.Users, target);

        // Act
        var report = await excel.ImportAsync(
            file,
            sheets.Build(),
            new ExcelWorkbookImportOptions { Strategy = ImportStrategy.Partial },
            Token);

        // Assert
        Assert.Equal(
            (ReportStatus.Failed, false, 1L),
            (report.Get(first)
                .Status, report.Get(first)
                .IsComplete, report.Get(first)
                .RowsImported));
    }

    [Fact]
    public async Task FurtherJoinedTargetWithAnExplicitGroupSize_IsAConfigurationError()
    {
        // Arrange
        await using var sqlite = await OpenAsync();
        await using var context = new TabularTestContext(Options(sqlite));
        await using var transaction = await context.Database.BeginTransactionAsync(Token);
        var database = TabularDatabase.ForContext(context);
        var first = TestMaps
            .Upsert(database)
            .WithOptions(new TabularWriteOptions { JoinExistingTransaction = true });

        var second = TestMaps
            .Upsert(database)
            .WithOptions(
                new TabularWriteOptions
                {
                    JoinExistingTransaction = true,
                    CommitGroupSize = 100,
                });

        var excel = new ExcelFormat(new ExcelFormatOptions());
        var export = ExcelExport.Create();
        export.Add(TabularSheetName.Create("First"), [new UserDto(1, "a@example.org", "Ada")], TestMaps.Users);
        export.Add(TabularSheetName.Create("Second"), [new UserDto(2, "b@example.org", "Bob")], TestMaps.Users);
        var file = new MemoryStream();
        await excel.ExportAsync(export.Build(), file, Token);
        file.Position = 0;
        var sheets = ExcelImport.Create();
        sheets.Add(TabularSheetName.Create("First"), TestMaps.Users, first);
        sheets.Add(TabularSheetName.Create("Second"), TestMaps.Users, second);

        // Act
        var report = await excel.ImportAsync(
            file,
            sheets.Build(),
            new ExcelWorkbookImportOptions { Strategy = ImportStrategy.Partial },
            Token);

        // Assert
        Assert.Equal(
            (ReportStatus.Failed, TabularErrorKind.Configuration),
            (report.Status, report.Errors[0].Kind));
    }

    /// <summary>A map of the second sheet whose rule runs <paramref name="reached" /> while the sheet is read.</summary>
    private static TabularMap<UserDto> CancelingMap(
        Action reached
    ) => TabularMap
        .For<UserDto>()
        .Column(
            x => x.PersonnelNumber,
            index: 0,
            configure: c => c.Validate(number =>
            {
                reached();

                return number > 0;
            }))
        .Column(x => x.Email, index: 1)
        .Column(x => x.Name, index: 2)
        .Build();

    /// <summary>A target whose create function runs <paramref name="second" /> for the user of the second sheet.</summary>
    private static TabularWriteTarget<UserDto, User> Target(
        TabularDatabase<TabularTestContext> database,
        Func<UserDto, User> second
    ) => database.PrepareUpsert<UserDto, User>(
        set: context => context.Users,
        match: TestMaps.UserMatch,
        create: dto => dto.PersonnelNumber == 2
            ? second(dto)
            : User.Create(dto.PersonnelNumber, dto.Email, dto.Name),
        update: (entity, dto) => entity
            .SetEmail(dto.Email)
            .SetName(dto.Name));

    private static async Task<(ExcelWorkbookImportReport Report, ExcelImportSheet<UserDto> First,
        ExcelImportSheet<UserDto> Second)> ImportAsync(
        TabularWriteTarget<UserDto> target,
        CancellationToken? cancellationToken = null,
        TabularMap<UserDto>? secondMap = null,
        string secondName = "Bob",
        ExcelFormatOptions? options = null
    )
    {
        var excel = new ExcelFormat(options ?? new ExcelFormatOptions());
        var export = ExcelExport.Create();
        export.Add(TabularSheetName.Create("First"), [new UserDto(1, "a@example.org", "Ada")], TestMaps.Users);
        export.Add(TabularSheetName.Create("Second"), [new UserDto(2, "b@example.org", secondName)], TestMaps.Users);
        var file = new MemoryStream();
        await excel.ExportAsync(export.Build(), file, Token);
        file.Position = 0;

        var sheets = ExcelImport.Create();
        var first = sheets.Add(TabularSheetName.Create("First"), TestMaps.Users, target);
        var second = sheets.Add(TabularSheetName.Create("Second"), secondMap ?? TestMaps.Users, target);
        var report = await excel.ImportAsync(
            file,
            sheets.Build(),
            new ExcelWorkbookImportOptions { Strategy = ImportStrategy.Partial },
            cancellationToken ?? Token);

        return (report, first, second);
    }

    private static async Task<SqliteFile> OpenAsync()
    {
        var sqlite = await SqliteFile.OpenAsync(Token);
        await using var setup = new TabularTestContext(Options(sqlite));
        await setup.Database.EnsureCreatedAsync(Token);

        return sqlite;
    }

    private static DbContextOptions<TabularTestContext> Options(
        SqliteFile sqlite
    ) => new DbContextOptionsBuilder<TabularTestContext>()
        .UseSqlite(sqlite.Connection)
        .Options;

    /// <summary>Fails the start of every transaction after the first commit.</summary>
    private sealed class FailStartAfterCommit : DbTransactionInterceptor
    {
        private bool _committed;

        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default
        )
        {
            _committed = true;

            return Task.CompletedTask;
        }

        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection,
            TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default
        ) => _committed
            ? throw new InvalidOperationException("The transaction could not be started.")
            : ValueTask.FromResult(result);
    }

    /// <summary>Cancels the call right after the first successful save and arms the failing rollback.</summary>
    private sealed class CancelAfterTheSave : SaveChangesInterceptor
    {
        private readonly CancellationTokenSource _cancellation;
        private readonly FailSavepointRollback _rollback;

        public CancelAfterTheSave(
            CancellationTokenSource cancellation,
            FailSavepointRollback rollback
        )
        {
            _cancellation = cancellation;
            _rollback = rollback;
        }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            if (!_rollback.Armed)
            {
                _rollback.Armed = true;
                _cancellation.Cancel();
            }

            return ValueTask.FromResult(result);
        }
    }

    /// <summary>A comparer that stops at its first use: it cancels the call or throws.</summary>
    private sealed class StoppingComparer : IEqualityComparer<int>
    {
        private readonly bool _cancel;
        private readonly CancellationTokenSource _cancellation;

        public StoppingComparer(
            CancellationTokenSource cancellation,
            bool cancel
        )
        {
            _cancellation = cancellation;
            _cancel = cancel;
        }

        public bool Equals(
            int x,
            int y
        ) => Stop();

        public int GetHashCode(
            int obj
        ) => Stop()
            ? obj
            : 0;

        private bool Stop()
        {
            if (!_cancel)
            {
                throw new InvalidOperationException("The comparer failed.");
            }

            _cancellation.Cancel();
            _cancellation.Token.ThrowIfCancellationRequested();

            return true;
        }
    }

    /// <summary>Fails the rollback of the transaction once armed, before the database undoes anything.</summary>
    private sealed class FailRollback : DbTransactionInterceptor
    {
        public bool Armed { get; set; }

        public override ValueTask<InterceptionResult> TransactionRollingBackAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default
        ) => Armed
            ? throw new InvalidOperationException("The rollback failed.")
            : ValueTask.FromResult(result);
    }

    /// <summary>Fails the rollback to a savepoint once armed, before the database undoes anything.</summary>
    private sealed class FailSavepointRollback : DbTransactionInterceptor
    {
        public bool Armed { get; set; }

        public override ValueTask<InterceptionResult> RollingBackToSavepointAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default
        ) => Armed
            ? throw new InvalidOperationException("The rollback to the savepoint failed.")
            : ValueTask.FromResult(result);
    }

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    private sealed record CityDto(
        string City
    );

    /// <summary>Rejects the save of an added address with the given city, naming only that address.</summary>
    private sealed class RejectCity : SaveChangesInterceptor
    {
        private readonly string _city;

        public RejectCity(
            string city
        )
        {
            _city = city;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            var rejected = Assert
                .IsType<TabularTestContext>(eventData.Context)
                .ChangeTracker
                .Entries<Address>()
                .Where(entry => entry.State == EntityState.Added && entry.Entity.City == _city)
                .ToList();

            if (rejected.Count > 0)
            {
                throw new DbUpdateException("The address was rejected.", [.. rejected]);
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed record Line(
        int Number,
        string Email,
        string Name,
        string City
    );
}
