using static Doka.Tabular.Tests.Infrastructure.TestFiles;

namespace Doka.Tabular.Tests;

/// <summary>The preparing part of a factory runs once at Build; every failure there is a configuration error.</summary>
public sealed partial class MapConstructionTests
{
    [Fact]
    public void ThrowingFactorySetup_IsAConfigurationError()
    {
        // Act
        var exception = Assert.Throws<TabularConfigurationException>(() => TabularMap
            .For<TwoConstructors>()
            .Column(x => x.Id, index: 0)
            .Column(x => x.Email, index: 1)
            .UseFactory(_ => throw new InvalidOperationException("Not ready."))
            .Build());

        // Assert
        Assert.DoesNotContain("Not ready.",
            Assert.Single(exception.Errors)
                .Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void FieldsKeptAfterBuild_RefuseFurtherProperties()
    {
        // Arrange
        var (_, kept) = KeptFieldsMap();

        // Act
        // WHY: Reading the export-only Email would make every later import fail, so the kept fields must refuse.
        var exception = Record.Exception(() => kept.Of(x => x.Email));

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
    }

    [Fact]
    public async Task FieldsKeptAfterBuild_CannotChangeTheBuiltMap()
    {
        // Arrange
        var (map, kept) = KeptFieldsMap();
        _ = Record.Exception(() => kept.Of(x => x.Email));
        var id = Guid.NewGuid();

        // Act
        var report = await CsvInstance.ImportAsync(TestInput.Utf8($"{id},a@example.org"), map, Token);

        // Assert
        Assert.Equal(new TwoConstructors(id, "none"), Assert.Single(report.Items!));
    }

    private static (TabularMap<TwoConstructors> Map, TabularFields<TwoConstructors> Kept) KeptFieldsMap()
    {
        TabularFields<TwoConstructors>? kept = null;
        var map = TabularMap
            .For<TwoConstructors>()
            .Column(x => x.Id, index: 0)
            .Column(x => x.Email, index: 1, configure: c => c.ExportOnly())
            .UseFactory(fields =>
            {
                kept = fields;
                var id = fields.Of(x => x.Id);

                return values => new TwoConstructors(values.Get(id), "none");
            })
            .Build();

        return (map, kept!);
    }

    [Fact]
    public void FieldsOfAThrowingFactorySetup_AreClosedToo()
    {
        // Arrange
        TabularFields<TwoConstructors>? kept = null;
        _ = Record.Exception(() => TabularMap
            .For<TwoConstructors>()
            .Column(x => x.Id, index: 0)
            .Column(x => x.Email, index: 1)
            .UseFactory(fields =>
            {
                kept = fields;

                throw new InvalidOperationException("Not ready.");
            })
            .Build());

        // Act
        var exception = Record.Exception(() => kept!.Of(x => x.Email));

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
    }

    [Fact]
    public void FactorySetupWithAnExpressionThatIsNoProperty_IsAConfigurationError()
    {
        // Act
        var exception = Record.Exception(() => TabularMap
            .For<TwoConstructors>()
            .Column(x => x.Id, index: 0)
            .Column(x => x.Email, index: 1)
            .UseFactory(fields =>
            {
                var email = fields.Of(x => x.Email.Trim());

                return values => new TwoConstructors(Guid.Empty, values.Get(email));
            })
            .Build());

        // Assert
        Assert.IsType<TabularConfigurationException>(exception);
    }

    [Fact]
    public void ValidFactorySetup_Builds()
    {
        // Act
        var exception = Record.Exception(() => TabularMap
            .For<TwoConstructors>()
            .Column(x => x.Id, index: 0)
            .Column(x => x.Email, index: 1)
            .UseFactory(fields =>
            {
                var id = fields.Of(x => x.Id);
                var email = fields.Of(x => x.Email);

                return values => new TwoConstructors(values.Get(id), values.Get(email));
            })
            .Build());

        // Assert
        Assert.Null(exception);
    }
}
