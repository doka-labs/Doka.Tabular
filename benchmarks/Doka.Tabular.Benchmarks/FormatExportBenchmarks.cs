namespace Doka.Tabular.Benchmarks;

/// <summary>Export of every format profile Tabular writes, CSV and XLSX Transitional; results are diagnostic only.</summary>
[MemoryDiagnoser]
[Config(typeof(ProfileConfig))]
public class FormatExportBenchmarks
{
    private ProfileRun? _run;

    // WHY: BenchmarkDotNet sets parameters through a public settable property.
    // ReSharper disable once MemberCanBePrivate.Global
    // ReSharper disable once AutoPropertyCanBeMadeGetOnly.Global
    // ReSharper disable once PropertyCanBeMadeInitOnly.Global
    [ParamsSource(nameof(Profiles))]
    public ReferenceProfile Profile { get; set; } = ReferenceProfile.CsvSmall;

    public static IEnumerable<ReferenceProfile> Profiles => ReferenceProfile
        .For(ProfileTarget.None)
        .Where(profile => profile.Exports);

    [GlobalSetup]
    public async Task SetupAsync() => _run = await ProfileRun.PrepareAsync(Profile);

    [Benchmark]
    public Task<ExportReport> Export()
        => (_run ?? throw new InvalidOperationException("The benchmark is not set up.")).ExportAsync(Stream.Null);
}
