namespace Doka.Tabular.EntityFrameworkCore.Specification;

/// <summary>Uncertain commits and early cancellation on every engine.</summary>
public abstract partial class ImportSpecification
{
    [Theory]
    [InlineData(Failure.CommitThrowsAfterwards, 4)]
    [InlineData(Failure.CommitRejected, 2)]
    public async Task Partial_FailedCommitOfAGroup_IsUncertainAndKeepsTheConfirmedGroups(
        Failure failure,
        int stored
    )
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        var faults = new TransactionFaults(failure);
        var target = TabularDatabase
            .ForFactory(database.FactoryWith(faults))
            .PrepareInsert<UserDto, User>(
                set: context => context.Users,
                create: dto =>
                {
                    // WHY: The commit of the second group (rows 3 and 4) fails; the first group is confirmed.
                    faults.Armed |= dto.PersonnelNumber == 3;

                    return User.Create(dto.PersonnelNumber, dto.Email, dto.Name);
                })
            .WithOptions(new TabularWriteOptions { CommitGroupSize = 2 });

        // Act
        var report = await s_csv.ImportAsync(
            TestMaps.Csv([.. TestMaps.Rows(5)]),
            TestMaps.Users,
            target,
            Partial,
            Token);

        // WHY: The check reads the stored rows, so the faults end with the import.
        faults.Armed = false;

        // Assert
        Assert.Equal(
            (TabularErrorCodes.Persistence.CommitUnknown, 2L, 2L),
            (report.FatalError?.Code, report.RowsImported, report.RowsUncertain));

        Assert.True(faults.Injected > 0);
        Assert.Equal(stored, (await database.UsersAsync()).Count);
    }

    [Fact]
    public async Task Strict_CancellationBeforeTheEnd_StoresNothing()
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
                    if (dto.PersonnelNumber == 3)
                    {
                        // ReSharper disable once AccessToDisposedClosure
                        // WHY: The callback runs during the awaited import, before the captured object is disposed.
                        cancellation.Cancel();
                    }

                    return User.Create(dto.PersonnelNumber, dto.Email, dto.Name);
                });

        // Act
        var report = await s_csv.ImportAsync(
            TestMaps.Csv([.. TestMaps.Rows(5)]),
            TestMaps.Users,
            target,
            new CsvImportOptions { Strategy = ImportStrategy.Strict },
            cancellation.Token);

        // Assert
        Assert.Equal((ReportStatus.Canceled, 0L), (report.Status, report.RowsImported));
        Assert.Empty(await database.UsersAsync());
    }
}
