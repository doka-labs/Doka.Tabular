using System.Linq;
using Doka.Tabular.Excel;
using Xunit;

namespace Doka.Tabular.EntityFrameworkCore.Tests;

/// <summary>An Excel import into a write target stores exactly the records the same import returns without one.</summary>
public sealed class ExcelDestinationTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ImportIntoATarget_StoresTheRecordsThatTheImportWithoutATargetReturns()
    {
        // Arrange
        await using var database = await SqliteFile.OpenAsync(Token);
        var options = new DbContextOptionsBuilder<TabularTestContext>()
            .UseSqlite(database.Connection)
            .Options;

        await using var context = new TabularTestContext(options);
        await context.Database.EnsureCreatedAsync(Token);
        var excel = new ExcelFormat(new ExcelFormatOptions());
        UserDto[] users = [new(1, "a@example.org", "Ada"), new(2, "b@example.org", "Bob")];
        var file = new MemoryStream();
        await excel.ExportAsync(users, TestMaps.Users, file, Token);

        // Act
        // WHY: The stored records are compared with those of the import without a target, so both imports form one
        // action.
        file.Position = 0;
        var returned = await excel.ImportAsync(file, TestMaps.Users, Token);
        file.Position = 0;
        var stored = await excel.ImportAsync(file,
            TestMaps.Users,
            TestMaps.Upsert(TabularDatabase.ForContext(context)),
            Token);

        // Assert
        Assert.Equal(users, returned.Items);
        Assert.Equal((ReportStatus.Succeeded, 2L), (stored.Status, stored.RowsImported));
        Assert.Equal(users.Select(user => (user.PersonnelNumber, user.Email, user.Name)),
            await context
                .Users
                .OrderBy(user => user.PersonnelNumber)
                .Select(user => ValueTuple.Create(user.PersonnelNumber, user.Email, user.Name))
                .ToListAsync(Token));
    }
}
