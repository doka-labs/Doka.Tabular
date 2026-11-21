namespace Doka.Tabular.Benchmarks;

/// <summary>Import into an EF Core target on SQLite; each measured import writes into a new database.</summary>
[MemoryDiagnoser]
[Config(typeof(ProfileConfig))]
public class StorageBenchmarks
{
    private ProfileDatabase? _database;
    private ProfileRun? _run;

    // WHY: BenchmarkDotNet sets parameters through a public settable property.
    // ReSharper disable once MemberCanBePrivate.Global
    // ReSharper disable once AutoPropertyCanBeMadeGetOnly.Global
    // ReSharper disable once PropertyCanBeMadeInitOnly.Global
    [ParamsSource(nameof(Profiles))]
    public ReferenceProfile Profile { get; set; } = ReferenceProfile
        .For(ProfileTarget.EntityFramework)
        .First();

    public static IEnumerable<ReferenceProfile> Profiles => ReferenceProfile.For(ProfileTarget.EntityFramework);

    /// <summary>Gets the database of the current iteration.</summary>
    private ProfileDatabase Database
        => _database ?? throw new InvalidOperationException("The iteration is not set up.");

    [GlobalSetup]
    public async Task SetupAsync() => _run = await ProfileRun.PrepareAsync(Profile);

    [IterationSetup]
    public void CreateDatabase() => _database = ProfileDatabase.Create();

    [IterationCleanup]
    public void DeleteDatabase()
    {
        _database?.Dispose();
        _database = null;
    }

    [Benchmark]
    public Task<ProfileOutcome> ImportIntoDatabase()
        => (_run ?? throw new InvalidOperationException("The benchmark is not set up.")).ImportIntoAsync(
            Database.Target());
}
