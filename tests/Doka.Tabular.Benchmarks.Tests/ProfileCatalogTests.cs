namespace Doka.Tabular.Benchmarks.Tests;

/// <summary>The catalog covers every required profile dimension, and every profile names all its parameters.</summary>
public sealed class ProfileCatalogTests
{
    private static readonly string[] s_parameterNames =
    [
        "format=", "shape=", "rows=", "columns=", "sheets=", "textLength=", "errorsPerThousand=", "selection=",
        "target=", "seed=",
    ];

    private static IReadOnlyList<ReferenceProfile> All => ReferenceProfile.All;

    [Fact]
    public void Profiles_HaveUniqueIdentifiers()
    {
        // Act
        var identifiers = All
            .Select(profile => profile.Id)
            .Distinct(StringComparer.Ordinal)
            .Count();

        // Assert
        Assert.Equal(All.Count, identifiers);
    }

    [Fact]
    public void Profiles_HaveAVersion()
    {
        // Act
        var versions = All.Select(profile => profile.Version);

        // Assert
        Assert.All(versions, version => Assert.True(version >= 1));
    }

    [Fact]
    public void Catalog_CoversSmallMediumAndLargeData()
    {
        // Act
        var sizes = All
            .Select(profile => profile.Rows)
            .ToList();

        // Assert
        Assert.Contains(sizes, rows => rows <= 1_000);
        Assert.Contains(sizes, rows => rows is >= 100_000 and < 1_000_000);
        Assert.Contains(sizes, rows => rows >= 1_000_000);
    }

    [Fact]
    public void Catalog_CoversEveryShapeFormatAndTarget()
    {
        // Act
        var shapes = All
            .Select(profile => profile.Shape)
            .ToHashSet();

        var formats = All
            .Select(profile => profile.Format)
            .ToHashSet();

        var targets = All
            .Select(profile => profile.Target)
            .ToHashSet();

        // Assert
        Assert.Equal(
            Enum
                .GetValues<ProfileShape>()
                .ToHashSet(),
            shapes);

        Assert.Equal(
            Enum
                .GetValues<ProfileFormat>()
                .ToHashSet(),
            formats);

        Assert.Equal(
            Enum
                .GetValues<ProfileTarget>()
                .ToHashSet(),
            targets);
    }

    [Fact]
    public void Catalog_CoversSheetsSelectionErrorsWideRowsAndLargeCsvAndXlsx()
    {
        // Act
        var multiSheet = All.Any(profile => profile.Sheets > 1);
        var selection = All.Any(profile => profile.ColumnSelection);
        var errors = All.Any(profile => profile.ErrorsPerThousand > 0);
        var wide = All.Any(profile => profile is { Shape: ProfileShape.Wide, Columns: > 100 });
        var large = All
            .Where(profile => profile.Rows >= 1_000_000)
            .Select(profile => profile.Format)
            .ToHashSet();

        // Assert
        Assert.Equal((true, true, true, true), (multiSheet, selection, errors, wide));
        Assert.Superset(
            new HashSet<ProfileFormat>
            {
                ProfileFormat.Csv,
                ProfileFormat.Xlsx,
            },
            large);
    }

    [Fact]
    public void ImportOnlyFormats_AreStrictAndXlsm()
    {
        // Act
        var importOnly = All
            .Where(profile => !profile.Exports)
            .Select(profile => profile.Format)
            .Distinct();

        // Assert
        Assert.Equal([ProfileFormat.XlsxStrict, ProfileFormat.Xlsm], importOnly);
    }

    [Fact]
    public void Parameters_NameEveryParameterOfTheProfile()
    {
        // Act
        var parameters = All.Select(profile => profile.Parameters);

        // Assert
        Assert.All(
            parameters,
            text => Assert.All(s_parameterNames, name => Assert.Contains(name, text, StringComparison.Ordinal)));
    }

    [Fact]
    public void ReportColumns_NameParametersSizeAndVersions()
    {
        // Act
        var values = ProfileColumn.All.ToDictionary(
            column => column.ColumnName,
            column => column.ValueOf(ReferenceProfile.CsvSmall));

        // Assert
        Assert.Equal(
            ["Parameters", "InputBytes", "Tabular", "RuntimeVersion", "EfCore", "SqliteProvider"],
            values.Keys);

        Assert.All(values.Values, value => Assert.False(string.IsNullOrWhiteSpace(value)));
        Assert.NotEqual("0", values["InputBytes"]);
    }
}
