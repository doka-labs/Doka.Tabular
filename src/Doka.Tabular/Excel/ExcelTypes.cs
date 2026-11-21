using System.Text.RegularExpressions;

namespace Doka.Tabular.Excel;

/// <summary>Expected Excel variant of an import.</summary>
[PublicAPI]
public enum ExcelVariant
{
    /// <summary>XLSX Transitional or XLSX Strict.</summary>
    Xlsx,

    /// <summary>XLSM; macros are neither run nor taken over.</summary>
    Xlsm,
}

/// <summary>Range or table object.</summary>
[PublicAPI]
public enum ExcelLayoutMode
{
    /// <summary>A plain cell range.</summary>
    Range,

    /// <summary>An Excel table object; requires a header row.</summary>
    Table,
}

/// <summary>Setting of the AutoFilter and the frozen header.</summary>
[PublicAPI]
public enum ExcelHeaderFeature
{
    /// <summary>Enabled when a header row and data rows exist.</summary>
    Auto,

    /// <summary>Enabled; requires a header row.</summary>
    Enabled,

    /// <summary>Disabled.</summary>
    Disabled,
}

/// <summary>Vertical alignment of header cells.</summary>
[PublicAPI]
public enum ExcelVerticalAlignment
{
    /// <summary>Top.</summary>
    Top,

    /// <summary>Center.</summary>
    Center,

    /// <summary>Bottom.</summary>
    Bottom,
}

/// <summary>Horizontal alignment of a column.</summary>
[PublicAPI]
public enum ExcelHorizontalAlignment
{
    /// <summary>Left.</summary>
    Left,

    /// <summary>Center.</summary>
    Center,

    /// <summary>Right.</summary>
    Right,
}

/// <summary>Opaque RGB color such as <c>#3A5A8C</c>.</summary>
[PublicAPI]
public sealed partial class ExcelColor : IEquatable<ExcelColor>
{
    private ExcelColor(
        string hex
    )
    {
        Hex = hex;
    }

    /// <summary>Gets the color as <c>#RRGGBB</c> in upper case.</summary>
    public string Hex { get; }

    internal string Argb => "FF" + Hex[1..];

    /// <inheritdoc />
    public bool Equals(
        ExcelColor? other
    ) => other is not null && string.Equals(Hex, other.Hex, StringComparison.Ordinal);

    /// <summary>Creates a color from <c>#RRGGBB</c>.</summary>
    /// <param name="value">The color text.</param>
    /// <returns>The color.</returns>
    /// <exception cref="ArgumentException">The text is not <c>#RRGGBB</c>.</exception>
    public static ExcelColor Create(
        string value
    )
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!RgbPattern()
            .IsMatch(value))
        {
            throw new ArgumentException("A color is given as #RRGGBB.", nameof(value));
        }

        return new ExcelColor(value.ToUpperInvariant());
    }

    /// <inheritdoc />
    public override bool Equals(
        object? obj
    ) => obj is ExcelColor other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Hex);

    /// <inheritdoc />
    public override string ToString() => Hex;

    [GeneratedRegex(@"\A#[0-9A-Fa-f]{6}\z", RegexOptions.CultureInvariant)]
    private static partial Regex RgbPattern();
}

/// <summary>
/// Name of an Excel table object: at most 255 characters, starting with a letter, underscore, or backslash, then
/// letters, digits, periods, and underscores, and not a cell reference.
/// </summary>
[PublicAPI]
public sealed partial class ExcelTableName : IEquatable<ExcelTableName>
{
    private ExcelTableName(
        string value
    )
    {
        Value = value;
    }

    /// <summary>Gets the name.</summary>
    public string Value { get; }

    /// <inheritdoc />
    public bool Equals(
        ExcelTableName? other
    ) => other is not null && string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    /// <summary>Creates a table name.</summary>
    /// <param name="value">The name.</param>
    /// <returns>The validated name.</returns>
    /// <exception cref="ArgumentException">The name violates the Excel rules.</exception>
    public static ExcelTableName Create(
        string value
    )
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length is 0 or > 255
            || !NamePattern()
                .IsMatch(value)
            || CellReference()
                .IsMatch(value)
            || value.Equals("R", StringComparison.OrdinalIgnoreCase)
            || value.Equals("C", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The table name violates the Excel naming rules.", nameof(value));
        }

        return new ExcelTableName(value);
    }

    internal static ExcelTableName Automatic(
        string value
    ) => new(value);

    /// <inheritdoc />
    public override bool Equals(
        object? obj
    ) => obj is ExcelTableName other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value);

    /// <inheritdoc />
    public override string ToString() => Value;

    [GeneratedRegex(@"\A[\p{L}_\\][\p{L}\p{Nd}._]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();

    [GeneratedRegex(@"\A([A-Za-z]{1,3}[0-9]+|[Rr][0-9]*[Cc][0-9]*)\z", RegexOptions.CultureInvariant)]
    private static partial Regex CellReference();
}

/// <summary>Layout of one exported column, addressed by its effective export key.</summary>
[PublicAPI]
public sealed record ExcelColumnLayout
{
    /// <summary>Gets the width in characters; it takes precedence over the estimate.</summary>
    public double? Width { get; init; }

    /// <summary>Gets the horizontal alignment; the default follows the cell type.</summary>
    public ExcelHorizontalAlignment? HorizontalAlignment { get; init; }
}

/// <summary>Layout of an exported sheet. A sheet layout replaces the call layout completely.</summary>
[PublicAPI]
public sealed record ExcelLayoutOptions
{
    /// <summary>Gets the first row, starting at 1; the default is 1.</summary>
    public int StartRow { get; init; } = 1;

    /// <summary>Gets the first column, starting at 1; the default is 1.</summary>
    public int StartColumn { get; init; } = 1;

    /// <summary>Gets range or table mode; the default is <see cref="ExcelLayoutMode.Range" />.</summary>
    public ExcelLayoutMode Mode { get; init; } = ExcelLayoutMode.Range;

    /// <summary>Gets the table name; only in table mode. <see langword="null" /> assigns <c>TabularExport</c>, <c>TabularExport_2</c>, and so on.</summary>
    public ExcelTableName? TableName { get; init; }

    /// <summary>Gets the table style; only in table mode. The default is <see cref="ExcelTableStyle.Medium2" />.</summary>
    public ExcelTableStyle? TableStyle { get; init; }

    /// <summary>Gets whether table rows are banded; only in table mode. The default is <see langword="true" />.</summary>
    public bool? BandedRows { get; init; }

    /// <summary>Gets the AutoFilter setting; the default is <see cref="ExcelHeaderFeature.Auto" />.</summary>
    public ExcelHeaderFeature Filter { get; init; } = ExcelHeaderFeature.Auto;

    /// <summary>Gets the frozen header setting; the default is <see cref="ExcelHeaderFeature.Auto" />.</summary>
    public ExcelHeaderFeature FreezeHeader { get; init; } = ExcelHeaderFeature.Auto;

    /// <summary>Gets the number of additionally frozen leading output columns; the default is 0.</summary>
    public int FrozenColumns { get; init; }

    /// <summary>Gets the upper bound of estimated widths in characters, at most 255; the default is 60.</summary>
    public double MaximumAutoWidth { get; init; } = 60;

    /// <summary>Gets the header height in points; <see langword="null" /> uses the Excel default height.</summary>
    public double? HeaderHeight { get; init; }

    /// <summary>Gets the header fill color.</summary>
    public ExcelColor? HeaderFill { get; init; }

    /// <summary>Gets the header font color.</summary>
    public ExcelColor? HeaderFontColor { get; init; }

    /// <summary>Gets whether the header font is bold.</summary>
    public bool? HeaderBold { get; init; }

    /// <summary>Gets the vertical alignment of the header.</summary>
    public ExcelVerticalAlignment? HeaderVerticalAlignment { get; init; }

    /// <summary>Gets per-column settings, resolved against the effective export keys.</summary>
    public IReadOnlyDictionary<ColumnKey, ExcelColumnLayout>? Columns { get; init; }
}
