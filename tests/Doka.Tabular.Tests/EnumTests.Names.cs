using System.Text.Json.Serialization;
using static Doka.Tabular.Tests.Infrastructure.TestFiles;

namespace Doka.Tabular.Tests;

/// <summary>
/// The import accepts C# names of flags values, so a separator inside a name is a configuration error; the key
/// function of a normal enum is asked only for its declared values; a collision names both values.
/// </summary>
public sealed partial class EnumTests
{
    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public enum FarNumbered
    {
        Draft = 1,

        [JsonStringEnumMemberName("7")]
        Published = 2,
    }

    [Flags]
    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public enum Joined
    {
        None = 0,
        Read = 1,
        Write = 2,

        // ReSharper disable once InconsistentNaming
        // WHY: The test needs a C# name that contains the separator.
        Read_Write = 3,
    }

    private static readonly TabularMap<Value<OrderBy>> s_declaredKeys = Map<OrderBy>(c => c.TranslationKeys(value
        => value switch
        {
            OrderBy.Ascending => "order.up",
            OrderBy.Descending => "order.down",
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "No such order."),
        }));

    private static readonly Dictionary<string, string> s_upDown = new()
    {
        ["order.up"] = "Up",
        ["order.down"] = "Down",
    };

    [Fact]
    public void FlagsSeparatorInsideAMemberName_IsAConfigurationError()
    {
        // Act
        var exception = Record.Exception(() => Map<Joined>(c => c.FlagsSeparator('_')));

        // Assert
        Assert.IsType<TabularConfigurationException>(exception);
    }

    [Fact]
    public async Task AnotherFlagsSeparator_WritesTheMemberName()
    {
        // Act
        var (_, text) = await ExportTextAsync(TabularType.Csv, Joined.Read_Write, PipedJoined());

        // Assert
        Assert.Equal("Read_Write", text);
    }

    [Fact]
    public async Task AnotherFlagsSeparator_ReadsTheMemberName()
    {
        // Act
        var report = await ReadAsync(TabularType.Csv, "Read_Write", PipedJoined());

        // Assert
        Assert.Equal(Joined.Read_Write, ValueOf(report));
    }

    private static TabularMap<Value<Joined>> PipedJoined() => Map<Joined>(c => c
        .FlagsSeparator('|')
        .ExportMemberName());

    [Fact]
    public async Task KeyFunctionOfANormalEnum_IsAskedOnlyForTheDeclaredValuesOnImport()
    {
        // Act
        var report = await CsvInstance.ImportAsync(
            TestInput.Utf8("1,Down"),
            s_declaredKeys,
            new CsvImportOptions { Translations = s_upDown },
            Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
        Assert.Equal(
            OrderBy.Descending,
            Assert.Single(report.Items!)
                .Item);
    }

    [Fact]
    public async Task KeyFunctionOfANormalEnum_IsAskedOnlyForTheDeclaredValuesOnExport()
    {
        // Act
        var report = await CsvInstance.ExportAsync(
            new[] { new Value<OrderBy>(1, OrderBy.Ascending) },
            s_declaredKeys,
            new MemoryStream(),
            new CsvExportOptions { Translations = s_upDown },
            Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
    }

    [Fact]
    public void CollidingTexts_NameBothValues()
    {
        // Act
        var error = Assert.Single(
            Assert
                .Throws<TabularConfigurationException>(() => Map<Colliding>())
                .Errors);

        // Assert
        Assert.Contains("Active and Enabled", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NumberShapedText_OfAnotherValue_NamesBothValuesInTheMessage()
    {
        // Act
        var exception = Record.Exception(() => Map<Numbered>(c => c.AllowNumericInput()));

        // Assert
        var message = Assert.Single(
                Assert
                    .IsType<TabularConfigurationException>(exception)
                    .Errors)
            .Message;

        Assert.Contains("Draft", message, StringComparison.Ordinal);
        Assert.Contains("Published", message, StringComparison.Ordinal);
    }

    [Fact]
    public void NumberShapedText_OfANumberWithoutMember_NamesTheNumberAndTheValue()
    {
        // Act
        var exception = Record.Exception(() => Map<FarNumbered>(c => c.AllowNumericInput()));

        // Assert
        var message = Assert.Single(
                Assert
                    .IsType<TabularConfigurationException>(exception)
                    .Errors)
            .Message;

        Assert.Contains("7", message, StringComparison.Ordinal);
        Assert.Contains("Published", message, StringComparison.Ordinal);
    }
}
