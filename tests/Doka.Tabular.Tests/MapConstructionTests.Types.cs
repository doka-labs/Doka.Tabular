namespace Doka.Tabular.Tests;

/// <summary>A column maps the type of its property; other types need Convert or a factory.</summary>
public sealed partial class MapConstructionTests
{
    [Theory]
    [InlineData("long")]
    [InlineData("int?")]
    [InlineData("cast")]
    [InlineData("nullable cast")]
    public void ColumnTypeOtherThanThePropertyType_IsAConfigurationError(
        string kind
    )
    {
        // Act
        var exception = Record.Exception(() => kind switch
        {
            "long" => TabularMap
                .For<Counted>()
                .Column<long>(x => x.Number, index: 0)
                .Build(),
            "int?" => TabularMap
                .For<Counted>()
                .Column<int?>(x => x.Number, index: 0)
                .Build(),
            "cast" => TabularMap
                .For<Wide>()
                .Column(x => (int)x.Number, index: 0)
                .Build(),
            _ => TabularMap
                .For<Optional>()
                .Column(x => (int)x.Number!, index: 0)
                .Build(),
        });

        // Assert
        Assert.IsType<TabularConfigurationException>(exception);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ColumnOfThePropertyTypeOrWithConvert_Builds(
        bool converted
    )
    {
        // Act
        var exception = Record.Exception(() => converted
            ? TabularMap
                .For<Counted>()
                .Column(x => x.Number,
                    index: 0,
                    configure: c => c.Convert<long, int>(value => checked((int)value), value => value))
                .Build()
            : TabularMap
                .For<Counted>()
                .Column(x => x.Number, index: 0)
                .Build());

        // Assert
        Assert.Null(exception);
    }

    // WHY: Only the map construction uses these records; no instance is created.
    // ReSharper disable ClassNeverInstantiated.Local
    private sealed record Counted(int Number);

    private sealed record Wide(long Number);

    private sealed record Optional(int? Number);

    // ReSharper restore ClassNeverInstantiated.Local
}
