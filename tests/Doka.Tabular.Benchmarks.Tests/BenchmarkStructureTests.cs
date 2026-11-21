using BenchmarkDotNet.Running;
using BenchmarkDotNet.Validators;

namespace Doka.Tabular.Benchmarks.Tests;

/// <summary>The benchmark declarations pass the BenchmarkDotNet validators before any measurement runs.</summary>
public sealed class BenchmarkStructureTests
{
    public static TheoryData<Type> Workloads => new(Types);

    private static Type[] Types { get; } = typeof(ReferenceProfile)
        .Assembly
        .GetTypes()
        .Where(type => type
            .GetMethods()
            .Any(method => method.IsDefined(typeof(BenchmarkAttribute))))
        .OrderBy(type => type.FullName, StringComparer.Ordinal)
        .ToArray();

    [Fact]
    public void Inventory_SeparatesFormatProcessingEfStorageAndFileStorage()
    {
        // Act
        var types = Types;

        // Assert
        Assert.Equal(
            [
                typeof(FileStoreBenchmarks), typeof(FormatExportBenchmarks), typeof(FormatImportBenchmarks),
                typeof(StorageBenchmarks),
            ],
            types);
    }

    [Theory]
    [MemberData(nameof(Workloads))]
    public void Workload_HasNoCriticalValidationError(
        Type type
    )
    {
        // Arrange
        using var discovery = BenchmarkConverter.TypeToBenchmarks(type);
        var validation = new ValidationParameters(discovery.BenchmarksCases, discovery.Config);

        // Act
        // WHY: Tests build Debug; the optimization check belongs to the explicit Release run of the benchmarks.
        var errors = discovery
            .Config
            .GetValidators()
            .Where(validator => validator is not JitOptimizationsValidator)
            .SelectMany(validator => validator.Validate(validation))
            .Where(error => error.IsCritical)
            .Select(error => error.Message)
            .ToList();

        // Assert
        Assert.False(type.IsSealed);
        Assert.Empty(errors);
        Assert.NotEmpty(discovery.BenchmarksCases);
    }
}
