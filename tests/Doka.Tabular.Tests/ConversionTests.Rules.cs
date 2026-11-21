using static Doka.Tabular.Tests.Infrastructure.TestFiles;

namespace Doka.Tabular.Tests;

/// <summary>Every violated rule of a field is its own error; one rule does not hide another.</summary>
public sealed partial class ConversionTests
{
    private static readonly TabularErrorCode s_positive = TabularErrorCode.Create("probe.positive");
    private static readonly TabularErrorCode s_even = TabularErrorCode.Create("probe.even");

    [Fact]
    public async Task EveryViolatedRuleOfAColumn_IsReported()
    {
        // Arrange
        var map = KeyedMap<int>(c => c
            .Validate(number => number > 0, s_positive)
            .Validate(number => number % 2 == 0, s_even));

        // Act
        var report = await CsvInstance.ImportAsync(TestInput.Utf8("1,-3\n2,-4\n3,4"), map, Token);

        // Assert
        Assert.Equal([s_positive, s_even, s_positive], report.Errors.Select(error => error.Code));
        Assert.Equal([1L, 1L, 2L], report.Errors.Select(error => error.Location.RecordNumber ?? 0));
    }

    [Fact]
    public async Task EveryViolatedRuleOfTheBaseType_IsReported()
    {
        // Arrange
        var map = KeyedMap<Wrapped>(c => c.Convert(number => new Wrapped($"{number}"),
            wrapped => int.Parse(wrapped.Text, CultureInfo.InvariantCulture),
            configure: b => b
                .Validate(number => number > 0, s_positive)
                .Validate(number => number % 2 == 0, s_even)));

        // Act
        var report = await CsvInstance.ImportAsync(TestInput.Utf8("1,-3\n2,4"), map, Token);

        // Assert
        Assert.Equal([s_positive, s_even], report.Errors.Select(error => error.Code));
        Assert.Equal(1L, report.RowsRejected);
    }

    [Fact]
    public async Task AThrowingRuleAfterAViolation_StillEndsTheImportTechnically()
    {
        // Arrange
        var map = KeyedMap<int>(c => c
            .Validate(number => number > 0, s_positive)
            .Validate(_ => throw new InvalidOperationException("broken")));

        // Act
        var report = await CsvInstance.ImportAsync(TestInput.Utf8("1,-3"), map, Token);

        // Assert
        Assert.Equal(TabularErrorCodes.Callback.Failed, report.FatalError?.Code);
        Assert.Contains(s_positive, report.Errors.Select(error => error.Code));
    }
}
