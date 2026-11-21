using System.Globalization;

namespace Doka.Tabular.Benchmarks;

public sealed record ProfileRow(
    int Id,
    string Name,
    decimal Amount,
    DateOnly Date,
    bool Active,
    string PostalCode,
    Guid Key
);

/// <summary>A wide row of sixteen embedded standard records.</summary>
public sealed record WideRow(
    ProfileRow P0,
    ProfileRow P1,
    ProfileRow P2,
    ProfileRow P3,
    ProfileRow P4,
    ProfileRow P5,
    ProfileRow P6,
    ProfileRow P7,
    ProfileRow P8,
    ProfileRow P9,
    ProfileRow P10,
    ProfileRow P11,
    ProfileRow P12,
    ProfileRow P13,
    ProfileRow P14,
    ProfileRow P15
)
{
    public const int Parts = 16;

    public static WideRow Of(
        IReadOnlyList<ProfileRow> parts
    ) => new(
        parts[0],
        parts[1],
        parts[2],
        parts[3],
        parts[4],
        parts[5],
        parts[6],
        parts[7],
        parts[8],
        parts[9],
        parts[10],
        parts[11],
        parts[12],
        parts[13],
        parts[14],
        parts[15]);
}

[Flags]
public enum Rights
{
    None = 0,
    Read = 1,
    Write = 2,
    Delete = 4,
}

/// <summary>Values whose text depends on culture, style, offset, unit, or label.</summary>
public sealed record ConversionRow(
    int Id,
    decimal Amount,
    decimal Share,
    DateTimeOffset At,
    TimeSpan Duration,
    Rights Rights,
    bool Approved,
    DateOnly Day
);

/// <summary>The three selected columns of a standard row, read back by header.</summary>
public sealed record SelectedRow(
    Guid Key,
    string Name,
    int Id
);

/// <summary>A standard row whose amount is text, so a file can carry invalid amounts.</summary>
public sealed record ErrorSourceRow(
    int Id,
    string Name,
    string Amount,
    DateOnly Date,
    bool Active,
    string PostalCode,
    Guid Key
);

/// <summary>The maps of the profiles.</summary>
public static class ProfileMaps
{
    public const int StandardColumns = 7;
    public const int ConversionColumns = 8;
    public const int SelectedColumns = 3;

    private static readonly CultureInfo s_german = CultureInfo.GetCultureInfo("de-DE");

    public static TabularMap<ProfileRow> Standard { get; } = TabularMap
        .For<ProfileRow>()
        .Column(x => x.Id, index: 0, key: ColumnKey.Create("id"), configure: c => c.Header("Id"))
        .Column(x => x.Name, index: 1, key: ColumnKey.Create("name"), configure: c => c.Header("Name"))
        .Column(x => x.Amount,
            index: 2,
            key: ColumnKey.Create("amount"),
            configure: c => c
                .Header("Amount")
                .ExcelNumberFormat("#,##0.00"))
        .Column(x => x.Date,
            index: 3,
            key: ColumnKey.Create("date"),
            configure: c => c
                .Header("Date")
                .ExcelNumberFormat("dd.mm.yyyy"))
        .Column(x => x.Active, index: 4, key: ColumnKey.Create("active"), configure: c => c.Header("Active"))
        .Column(x => x.PostalCode,
            index: 5,
            key: ColumnKey.Create("postalCode"),
            configure: c => c.Header("PostalCode"))
        .Column(x => x.Key, index: 6, key: ColumnKey.Create("key"), configure: c => c.Header("Key"))
        .Build();

    /// <summary>Gets the selected columns of the export, in their order.</summary>
    public static IReadOnlyList<ColumnKey> Selection { get; } =
        [ColumnKey.Create("key"), ColumnKey.Create("name"), ColumnKey.Create("id")];

    public static TabularMap<SelectedRow> Selected { get; } = TabularMap
        .For<SelectedRow>()
        .Column(x => x.Key, index: 0, configure: c => c.Header("Key"))
        .Column(x => x.Name, index: 1, configure: c => c.Header("Name"))
        .Column(x => x.Id, index: 2, configure: c => c.Header("Id"))
        .Build();

    public static TabularMap<ErrorSourceRow> ErrorSource { get; } = TabularMap
        .For<ErrorSourceRow>()
        .Column(x => x.Id, index: 0, configure: c => c.Header("Id"))
        .Column(x => x.Name, index: 1, configure: c => c.Header("Name"))
        .Column(x => x.Amount, index: 2, configure: c => c.Header("Amount"))
        .Column(x => x.Date,
            index: 3,
            configure: c => c
                .Header("Date")
                .ExcelNumberFormat("dd.mm.yyyy"))
        .Column(x => x.Active, index: 4, configure: c => c.Header("Active"))
        .Column(x => x.PostalCode, index: 5, configure: c => c.Header("PostalCode"))
        .Column(x => x.Key, index: 6, configure: c => c.Header("Key"))
        .Build();

    public static TabularMap<WideRow> Wide { get; } = TabularMap
        .For<WideRow>()
        .Nested(x => x.P0,
            Standard,
            e => e
                .StartIndex(0)
                .KeyPrefix(ColumnKey.Create("p0"))
                .HeaderPrefix("P0 "))
        .Nested(x => x.P1,
            Standard,
            e => e
                .StartIndex(7)
                .KeyPrefix(ColumnKey.Create("p1"))
                .HeaderPrefix("P1 "))
        .Nested(x => x.P2,
            Standard,
            e => e
                .StartIndex(14)
                .KeyPrefix(ColumnKey.Create("p2"))
                .HeaderPrefix("P2 "))
        .Nested(x => x.P3,
            Standard,
            e => e
                .StartIndex(21)
                .KeyPrefix(ColumnKey.Create("p3"))
                .HeaderPrefix("P3 "))
        .Nested(x => x.P4,
            Standard,
            e => e
                .StartIndex(28)
                .KeyPrefix(ColumnKey.Create("p4"))
                .HeaderPrefix("P4 "))
        .Nested(x => x.P5,
            Standard,
            e => e
                .StartIndex(35)
                .KeyPrefix(ColumnKey.Create("p5"))
                .HeaderPrefix("P5 "))
        .Nested(x => x.P6,
            Standard,
            e => e
                .StartIndex(42)
                .KeyPrefix(ColumnKey.Create("p6"))
                .HeaderPrefix("P6 "))
        .Nested(x => x.P7,
            Standard,
            e => e
                .StartIndex(49)
                .KeyPrefix(ColumnKey.Create("p7"))
                .HeaderPrefix("P7 "))
        .Nested(x => x.P8,
            Standard,
            e => e
                .StartIndex(56)
                .KeyPrefix(ColumnKey.Create("p8"))
                .HeaderPrefix("P8 "))
        .Nested(x => x.P9,
            Standard,
            e => e
                .StartIndex(63)
                .KeyPrefix(ColumnKey.Create("p9"))
                .HeaderPrefix("P9 "))
        .Nested(x => x.P10,
            Standard,
            e => e
                .StartIndex(70)
                .KeyPrefix(ColumnKey.Create("p10"))
                .HeaderPrefix("P10 "))
        .Nested(x => x.P11,
            Standard,
            e => e
                .StartIndex(77)
                .KeyPrefix(ColumnKey.Create("p11"))
                .HeaderPrefix("P11 "))
        .Nested(x => x.P12,
            Standard,
            e => e
                .StartIndex(84)
                .KeyPrefix(ColumnKey.Create("p12"))
                .HeaderPrefix("P12 "))
        .Nested(x => x.P13,
            Standard,
            e => e
                .StartIndex(91)
                .KeyPrefix(ColumnKey.Create("p13"))
                .HeaderPrefix("P13 "))
        .Nested(x => x.P14,
            Standard,
            e => e
                .StartIndex(98)
                .KeyPrefix(ColumnKey.Create("p14"))
                .HeaderPrefix("P14 "))
        .Nested(x => x.P15,
            Standard,
            e => e
                .StartIndex(105)
                .KeyPrefix(ColumnKey.Create("p15"))
                .HeaderPrefix("P15 "))
        .Build();

    public static TabularMap<ConversionRow> Conversion { get; } = TabularMap
        .For<ConversionRow>()
        .Column(x => x.Id, index: 0, configure: c => c.Header("Id"))
        .Column(x => x.Amount,
            index: 1,
            configure: c => c
                .Header("Betrag")
                .Culture(s_german)
                .Rounding(2))
        .Column(x => x.Share,
            index: 2,
            configure: c => c
                .Header("Anteil")
                .Culture(s_german)
                .TextStyle(NumberTextStyle.Percent))
        .Column(x => x.At, index: 3, configure: c => c.Header("Zeitpunkt"))
        .Column(x => x.Duration, index: 4, configure: c => c.Header("Dauer"))
        .Column(x => x.Rights,
            index: 5,
            configure: c => c
                .Header("Rechte")
                .FlagsSeparator('|'))
        .Column(x => x.Approved,
            index: 6,
            configure: c => c
                .Header("Freigegeben")
                .TrueText("ja")
                .FalseText("nein"))
        .Column(x => x.Day,
            index: 7,
            configure: c => c
                .Header("Tag")
                .Culture(s_german)
                .Format("dd.MM.yyyy"))
        .Build();
}
