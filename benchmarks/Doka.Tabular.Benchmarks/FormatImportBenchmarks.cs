namespace Doka.Tabular.Benchmarks;

/// <summary>Import of every format profile without a target; results are diagnostic only.</summary>
[MemoryDiagnoser]
[Config(typeof(ProfileConfig))]
public class FormatImportBenchmarks
{
    private ProfileRun? _run;

    // WHY: BenchmarkDotNet sets parameters through a public settable property.
    // ReSharper disable once MemberCanBePrivate.Global
    // ReSharper disable once AutoPropertyCanBeMadeGetOnly.Global
    // ReSharper disable once PropertyCanBeMadeInitOnly.Global
    [ParamsSource(nameof(Profiles))]
    public ReferenceProfile Profile { get; set; } = ReferenceProfile.CsvSmall;

    public static IEnumerable<ReferenceProfile> Profiles => ReferenceProfile.For(ProfileTarget.None);

    [GlobalSetup]
    public async Task SetupAsync() => _run = await ProfileRun.PrepareAsync(Profile);

    [Benchmark]
    public Task<ProfileOutcome> Import()
        => (_run ?? throw new InvalidOperationException("The benchmark is not set up.")).ImportAsync();
}
