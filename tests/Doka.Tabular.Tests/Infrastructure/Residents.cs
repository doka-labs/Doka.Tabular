// WHY: The core, the CSV, and the Excel test applications link this file; each uses only some of its members.
// ReSharper disable UnusedMember.Global

namespace Doka.Tabular.Tests.Infrastructure;

/// <summary>A record of the overload tests; the city is required on import.</summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
internal sealed record Resident(
    string Name,
    string? City
);

/// <summary>A source record of a row export map, holding one resident.</summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
internal sealed record ResidentRow(
    Resident Resident
);

/// <summary>The maps and records every overload test shares.</summary>
internal static class Residents
{
    public static readonly ColumnKey NameKey = ColumnKey.Create("name");

    public static readonly TabularMap<Resident> Map = TabularMap
        .For<Resident>()
        .Column(x => x.Name, index: 0, key: NameKey)
        .Column(x => x.City, index: 1, key: ColumnKey.Create("city"), configure: c => c.Required())
        .Build();

    public static readonly Resident[] Records = [new("Ada", "Dresden"), new("Bob", "Bonn")];

    private static readonly RowMapBuilder s_rows = RowMap.Create();

    public static readonly TabularRole<Resident> Role = s_rows.Add(Map);

    public static readonly RowMap RowMap = s_rows.Build();

    public static readonly RowExportMap<ResidentRow> RowExport = RowMap
        .From<ResidentRow>()
        .Bind(Role, x => x.Resident)
        .Build();
}
