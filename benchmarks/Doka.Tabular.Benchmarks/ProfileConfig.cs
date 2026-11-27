using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Doka.Tabular.Benchmarks;

/// <summary>
/// The report columns of every profile benchmark: profile version and parameters, input file size, and the versions of
/// Tabular, the runtime, EF Core, and the SQLite provider.
/// </summary>
public sealed class ProfileConfig : ManualConfig
{
    public ProfileConfig()
    {
        AddColumn([.. ProfileColumn.All]);
    }
}

/// <summary>One report column whose value follows from the profile of a benchmark case.</summary>
public sealed class ProfileColumn : IColumn
{
    private static readonly ConcurrentDictionary<ReferenceProfile, long> s_sizes = new();
    private readonly Func<ReferenceProfile, string> _value;

    private ProfileColumn(
        string name,
        string legend,
        Func<ReferenceProfile, string> value
    )
    {
        ColumnName = name;
        Legend = legend;
        _value = value;
    }

    public static IReadOnlyList<ProfileColumn> All { get; } =
    [
        new("Parameters", "Every parameter of the reference profile", profile => profile.Parameters),
        new(
            "InputBytes",
            "Size of the generated input file",
            profile => InputBytes(profile)
                .ToString("N0", CultureInfo.InvariantCulture)),
        new("Tabular", "Version of Doka.Tabular", _ => VersionOf(typeof(CsvFormat).Assembly)),
        new("RuntimeVersion", "Runtime of the measurement", _ => RuntimeInformation.FrameworkDescription),
        new("EfCore", "Version of EF Core", _ => VersionOf(typeof(DbContext).Assembly)),
        new(
            "SqliteProvider",
            "Versions of the EF Core SQLite provider and Microsoft.Data.Sqlite",
            _ => $"{VersionOf(typeof(SqliteDbContextOptionsBuilderExtensions).Assembly)} / {VersionOf(typeof(SqliteConnection).Assembly)}"),
    ];

    public string Id => nameof(ProfileColumn) + "." + ColumnName;

    public string ColumnName { get; }

    public bool AlwaysShow => true;

    public ColumnCategory Category => ColumnCategory.Params;

    public int PriorityInCategory => 0;

    public bool IsNumeric => false;

    public UnitType UnitType => UnitType.Dimensionless;

    public string Legend { get; }

    public string GetValue(
        Summary summary,
        BenchmarkCase benchmarkCase
    ) => benchmarkCase.Parameters["Profile"] is ReferenceProfile profile
        ? _value(profile)
        : string.Empty;

    public string GetValue(
        Summary summary,
        BenchmarkCase benchmarkCase,
        SummaryStyle style
    ) => GetValue(summary, benchmarkCase);

    public bool IsDefault(
        Summary summary,
        BenchmarkCase benchmarkCase
    ) => false;

    public bool IsAvailable(
        Summary summary
    ) => true;

    /// <summary>The value of the column for a profile, also outside a summary.</summary>
    public string ValueOf(
        ReferenceProfile profile
    ) => _value(profile);

    /// <summary>The size of the input file; the profile is generated once per report, not per measurement.</summary>
    private static long InputBytes(
        ReferenceProfile profile
    ) => s_sizes.GetOrAdd(
        profile,
        key => ProfileRun
            .PrepareAsync(key)
            .GetAwaiter()
            .GetResult()
            .Input
            .LongLength);

    private static string VersionOf(
        Assembly assembly
    ) => assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? assembly
            .GetName()
            .Version
            ?.ToString()
        ?? "unknown";
}
