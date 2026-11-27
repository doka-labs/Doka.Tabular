using Xunit;

namespace Doka.Tabular.EntityFrameworkCore.Tests;

/// <summary>
/// The standard journal compares import scopes as its registration sets them: ordinally by default, without regard to
/// case with <see cref="TabularJournalSettings.IgnoreImportScopeCase" />.
/// </summary>
public sealed partial class JournalWindowTests
{
    [Theory]
    [InlineData(false, ImportJournalDecision.Processed)]
    [InlineData(true, ImportJournalDecision.Skipped)]
    public async Task StandardJournal_ComparesImportScopesAsRegistered(
        bool ignoreImportScopeCase,
        ImportJournalDecision expected
    )
    {
        // Arrange
        await using var database = await FileDatabase.CreateAsync();
        var factory = new OptionsContextFactory(
            new DbContextOptionsBuilder<TabularTestContext>()
                .UseSqlite(database.ConnectionString)
                .UseTabularJournal(settings => settings.IgnoreImportScopeCase = ignoreImportScopeCase)
                .Options);

        var journal = TabularJournal.ForFactory(factory);
        await ImportInScopeAsync(factory, journal, "Users");

        // Act
        var repeated = await ImportInScopeAsync(factory, journal, "users");

        // Assert
        Assert.Equal(expected, repeated.Journal?.Decision);
    }

    private static Task<ImportReport<UserDto>> ImportInScopeAsync(
        IDbContextFactory<TabularTestContext> data,
        TabularJournal journal,
        string scope
    ) => s_csv.ImportAsync(
        TestMaps.Csv("1,a@example.org,Ada"),
        TestMaps.Users,
        TestMaps
            .Upsert(TabularDatabase.ForFactory(data))
            .WithImportLog(journal, new TabularImportLogOptions { Scope = JournalScope.Create(scope) }),
        Token);
}
