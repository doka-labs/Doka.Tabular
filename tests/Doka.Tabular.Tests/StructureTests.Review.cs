using static Doka.Tabular.Tests.Infrastructure.TestFiles;

namespace Doka.Tabular.Tests;

/// <summary>Export-only substructures on import and conflicting null representations.</summary>
public sealed partial class StructureTests
{
    [Fact]
    public async Task ExportOnlySubstructure_DoesNotBlockTheImport()
    {
        // Arrange
        var address = TabularMap
            .For<ExportedAddress>()
            .Column(x => x.Item, index: 0, configure: c => c.ExportOnly())
            .Build();

        var map = TabularMap
            .For<HolderWithAddress>()
            .Column(x => x.Name, index: 0, configure: c => c.Header("Name"))
            .Nested(x => x.Address, address, e => e.StartIndex(1))
            .Build();

        // Act
        var report = await CsvInstance.ImportAsync(
            TestInput.Utf8("Name\nAda"),
            map,
            new CsvImportOptions { Binding = ColumnBinding.ByHeaderName },
            Token);

        // Assert
        Assert.True(
            report.Status == ReportStatus.Succeeded,
            string.Join(" | ", report.Errors.Select(error => error.Message)));

        Assert.Equal(
            "Ada",
            Assert.Single(report.Items!)
                .Name);
    }

    [Fact]
    public void EmptyTranslationKeyWithEmptyStringAsNull_IsAConfigurationError()
    {
        // Act
        var exception = Record.Exception(() => TabularMap
            .For<OptionalName>()
            .Column(
                x => x.Name,
                index: 0,
                configure: c => c
                    .EmptyTranslationKey("name.empty")
                    .EmptyStringAsNull())
            .Build());

        // Assert
        Assert.IsType<TabularConfigurationException>(exception);
    }

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public sealed record ExportedAddress(string? Item);

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public sealed class HolderWithAddress
    {
        public string Name { get; init; } = string.Empty;

        public ExportedAddress? Address { get; init; }
    }

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public sealed record OptionalName(string? Name);
}
