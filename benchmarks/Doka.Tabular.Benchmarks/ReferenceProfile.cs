namespace Doka.Tabular.Benchmarks;

/// <summary>The file format of a profile; Strict and XLSM are import formats only.</summary>
public enum ProfileFormat
{
    Csv,
    Xlsx,
    XlsxStrict,
    Xlsm,
}

/// <summary>The kind of data of a profile.</summary>
public enum ProfileShape
{
    /// <summary>Seven columns of common types.</summary>
    Standard,

    /// <summary>Sixteen embedded standard records, 112 columns.</summary>
    Wide,

    /// <summary>The standard columns with a distinct text of random length and characters in every row.</summary>
    Texts,

    /// <summary>Culture-dependent numbers, percent, offsets, durations, flags, and labels.</summary>
    Conversions,
}

/// <summary>Where the imported records go, or where the exported file is stored.</summary>
public enum ProfileTarget
{
    /// <summary>Format processing only.</summary>
    None,

    /// <summary>Import into an EF Core target on SQLite.</summary>
    EntityFramework,

    /// <summary>Export into a file store.</summary>
    FileStore,
}

/// <summary>
/// A generated, reproducible data set: identifier, version, and every parameter are fixed, so input and expected
/// result follow from the profile alone and never adapt to the machine.
/// </summary>
/// <param name="Id">The identifier of the profile.</param>
/// <param name="Version">The version; it changes with any change of the generated data.</param>
/// <param name="Format">The file format.</param>
/// <param name="Shape">The kind of data.</param>
/// <param name="Rows">The number of records over all sheets.</param>
/// <param name="Sheets">The number of sheets; above one an Excel workbook with equal parts.</param>
/// <param name="TextLength">The text length; for <see cref="ProfileShape.Texts" /> the largest one.</param>
/// <param name="ErrorsPerThousand">The share of records with an invalid amount, drawn with the seed.</param>
/// <param name="ColumnSelection">Whether the export selects and reorders three columns, read back by header.</param>
/// <param name="Target">The target of the records or the file.</param>
/// <param name="Seed">The seed of every random value.</param>
public sealed record ReferenceProfile(
    string Id,
    int Version,
    ProfileFormat Format,
    ProfileShape Shape,
    int Rows,
    int Sheets,
    int TextLength,
    int ErrorsPerThousand,
    bool ColumnSelection,
    ProfileTarget Target,
    int Seed
)
{
    private const int Small = 1_000;
    private const int Medium = 100_000;
    private const int Large = 1_000_000;

    public static ReferenceProfile CsvSmall { get; } = Of("csv-small", ProfileFormat.Csv, Small);

    public static ReferenceProfile CsvLarge { get; } = Of("csv-large", ProfileFormat.Csv, Large);

    public static ReferenceProfile XlsxLarge { get; } = Of("xlsx-large", ProfileFormat.Xlsx, Large);

    /// <summary>Gets every profile in a fixed order.</summary>
    public static IReadOnlyList<ReferenceProfile> All { get; } =
    [
        CsvSmall,
        Of("csv-medium", ProfileFormat.Csv, Medium),
        CsvLarge,
        Of("xlsx-small", ProfileFormat.Xlsx, Small),
        Of("xlsx-medium", ProfileFormat.Xlsx, Medium),
        XlsxLarge,
        Of("xlsx-strict-medium", ProfileFormat.XlsxStrict, Medium),
        Of("xlsm-medium", ProfileFormat.Xlsm, Medium),
        Of("csv-wide", ProfileFormat.Csv, 10_000) with { Shape = ProfileShape.Wide },
        Of("xlsx-wide", ProfileFormat.Xlsx, 10_000) with { Shape = ProfileShape.Wide },
        Of("csv-texts", ProfileFormat.Csv, Medium) with
        {
            Shape = ProfileShape.Texts,
            TextLength = 64,
        },
        Of("xlsx-texts", ProfileFormat.Xlsx, Medium) with
        {
            Shape = ProfileShape.Texts,
            TextLength = 64,
        },
        Of("csv-conversions", ProfileFormat.Csv, Medium) with { Shape = ProfileShape.Conversions },
        Of("xlsx-conversions", ProfileFormat.Xlsx, Medium) with { Shape = ProfileShape.Conversions },
        Of("xlsx-workbook", ProfileFormat.Xlsx, 150_000) with { Sheets = 3 },
        Of("csv-selection", ProfileFormat.Csv, Medium) with { ColumnSelection = true },
        Of("xlsx-selection", ProfileFormat.Xlsx, Medium) with { ColumnSelection = true },
        Of("csv-errors", ProfileFormat.Csv, Medium) with { ErrorsPerThousand = 10 },
        Of("xlsx-errors", ProfileFormat.Xlsx, Medium) with { ErrorsPerThousand = 10 },
        Of("ef-csv-medium", ProfileFormat.Csv, Medium) with { Target = ProfileTarget.EntityFramework },
        Of("ef-xlsx-medium", ProfileFormat.Xlsx, Medium) with { Target = ProfileTarget.EntityFramework },
        Of("store-csv-medium", ProfileFormat.Csv, Medium) with { Target = ProfileTarget.FileStore },
        Of("store-xlsx-medium", ProfileFormat.Xlsx, Medium) with { Target = ProfileTarget.FileStore },
    ];

    /// <summary>Gets the number of columns of the file.</summary>
    public int Columns => Shape switch
    {
        _ when ColumnSelection => ProfileMaps.SelectedColumns,
        ProfileShape.Wide => WideRow.Parts * ProfileMaps.StandardColumns,
        ProfileShape.Conversions => ProfileMaps.ConversionColumns,
        _ => ProfileMaps.StandardColumns,
    };

    /// <summary>Gets whether Tabular also writes this format; Strict and XLSM are read only.</summary>
    public bool Exports => Format is ProfileFormat.Csv or ProfileFormat.Xlsx;

    /// <summary>Gets every parameter as text, for measurement reports.</summary>
    public string Parameters => $"format={Format}, shape={Shape}, rows={Rows}, columns={Columns}, sheets={Sheets}, "
        + $"textLength={TextLength}, errorsPerThousand={ErrorsPerThousand}, selection={ColumnSelection}, "
        + $"target={Target}, seed={Seed}";

    /// <summary>Gets the profiles of one target, the parameter source of a benchmark class.</summary>
    public static IEnumerable<ReferenceProfile> For(
        ProfileTarget target
    ) => All.Where(profile => profile.Target == target);

    public override string ToString() => $"{Id}-v{Version}";

    private static ReferenceProfile Of(
        string id,
        ProfileFormat format,
        int rows
    ) => new(
        id,
        1,
        format,
        ProfileShape.Standard,
        rows,
        1,
        12,
        0,
        false,
        ProfileTarget.None,
        42);
}
