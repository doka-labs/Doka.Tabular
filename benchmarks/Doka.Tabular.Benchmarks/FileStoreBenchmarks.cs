namespace Doka.Tabular.Benchmarks;

/// <summary>Export into a file target, buffered and stored through the store interface; the store keeps it in memory.</summary>
[MemoryDiagnoser]
[Config(typeof(ProfileConfig))]
public class FileStoreBenchmarks
{
    private ProfileRun? _run;

    // WHY: BenchmarkDotNet sets parameters through a public settable property.
    // ReSharper disable once MemberCanBePrivate.Global
    // ReSharper disable once AutoPropertyCanBeMadeGetOnly.Global
    // ReSharper disable once PropertyCanBeMadeInitOnly.Global
    [ParamsSource(nameof(Profiles))]
    public ReferenceProfile Profile { get; set; } = ReferenceProfile
        .For(ProfileTarget.FileStore)
        .First();

    public static IEnumerable<ReferenceProfile> Profiles => ReferenceProfile.For(ProfileTarget.FileStore);

    [GlobalSetup]
    public async Task SetupAsync() => _run = await ProfileRun.PrepareAsync(Profile);

    [Benchmark]
    public Task<(ExportReport Report, MemoryStore Store)> ExportToStore()
        => (_run ?? throw new InvalidOperationException("The benchmark is not set up.")).ExportToStoreAsync();
}
