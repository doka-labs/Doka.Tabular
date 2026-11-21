namespace Doka.Tabular;

internal enum FieldDirection
{
    Both,
    ImportOnly,
    ExportOnly,

    /// <summary>An export-only field inside an import-only role: neither read nor written.</summary>
    None,
}

internal enum MaskMode
{
    ExceptLast,
    ExceptFirst,
}

internal sealed record FieldMask(
    MaskMode Mode,
    int VisibleCount,
    char MaskCharacter
);

internal sealed record FieldPadding(
    int TotalWidth,
    char PaddingCharacter
);

internal sealed record NumericRounding(
    int? Decimals,
    int? SignificantDigits,
    MidpointRounding Mode
);

internal sealed record TimeRounding(
    TimeSpan Step,
    MidpointRounding Mode
);

/// <summary>All per-field settings in type-independent form; frozen by <c>Build()</c>.</summary>
internal sealed class FieldSettings
{
    public string? Header { get; set; }

    public string? HeaderTranslationKey { get; set; }

    public bool IsRequired { get; set; }

    public List<FieldValidator> Validators { get; private set; } = [];

    public bool IncludeSourceValue { get; set; }

    public bool? Trim { get; set; }

    public CultureInfo? Culture { get; set; }

    public FieldDirection Direction { get; set; }

    public bool IsHeaderOptional { get; set; }

    public bool RejectFormulas { get; set; }

    public string? ExcelNumberFormat { get; set; }

    public bool ExcelAsText { get; set; }

    public string? NullText { get; set; }

    public string? EmptyTranslationKey { get; set; }

    public bool EmptyStringAsNull { get; set; }

    public FieldMask? Mask { get; set; }

    public FieldPadding? Padding { get; set; }

    public string? TrueText { get; set; }

    public string? FalseText { get; set; }

    public string? TrueTranslationKey { get; set; }

    public string? FalseTranslationKey { get; set; }

    public Func<object, string>? EnumTranslationKey { get; set; }

    public bool AllowNumericInput { get; set; }

    public bool ExportAsNumber { get; set; }

    public bool ExportMemberName { get; set; }

    public List<string> ExportAliases { get; private set; } = [];

    public char? FlagsSeparator { get; set; }

    public bool AllowDecimalPoint { get; set; }

    public bool AllowExponent { get; set; }

    public NumberTextStyle TextStyle { get; set; }

    public string? ExportFormat { get; set; }

    public NumericRounding? NumericRounding { get; set; }

    public bool AllowNonFiniteValues { get; set; }

    public string? TimeFormat { get; set; }

    public TimeZoneInfo? FileTimeZone { get; set; }

    public AmbiguousTimeResolution? AmbiguousTimeResolution { get; set; }

    public bool ShiftInvalidTime { get; set; }

    public bool ExcelAsUtc { get; set; }

    public bool AllowExcelSerialNumbers { get; set; }

    public ExcelDurationUnit ExcelDurationUnit { get; set; } = ExcelDurationUnit.Days;

    public TimeRounding? TimeRounding { get; set; }

    public string? GuidInputFormats { get; set; }

    public string? GuidOutputFormat { get; set; }

    public CustomConversion? Conversion { get; set; }

    private List<string> ConfigurationProblems { get; set; } = [];

    /// <summary>
    /// The settings the conversion of the field is checked with: those of the base type of a custom conversion, but
    /// with the trim setting of the field, which the reader applies first.
    /// </summary>
    public FieldSettings CheckedConversionSettings()
    {
        var conversion = Conversion?.BaseSettings ?? this;
        if (Trim is null
            || conversion.Trim == Trim)
        {
            return conversion;
        }

        var copy = conversion.Clone();
        copy.Trim = Trim;

        return copy;
    }

    /// <summary>
    /// The settings of the value conversion that are set on this level. On a column with a custom conversion only the
    /// base type is converted, so these settings belong to the builder of the base type.
    /// </summary>
    public IEnumerable<string> ConversionSettings()
    {
        (bool IsSet, string Name)[] settings =
        [
            (Culture is not null, nameof(Culture)),
            (ExcelNumberFormat is not null, nameof(ExcelNumberFormat)),
            (Padding is not null, "PadLeft"),
            (TrueText is not null, nameof(TrueText)),
            (FalseText is not null, nameof(FalseText)),
            (TrueTranslationKey is not null || FalseTranslationKey is not null || EnumTranslationKey is not null,
                "TranslationKeys"),
            (AllowNumericInput, nameof(AllowNumericInput)),
            (ExportAsNumber, nameof(ExportAsNumber)),
            (ExportMemberName, nameof(ExportMemberName)),
            (ExportAliases.Count > 0, "ExportAlias"),
            (FlagsSeparator is not null, nameof(FlagsSeparator)),
            (AllowDecimalPoint, nameof(AllowDecimalPoint)),
            (AllowExponent, nameof(AllowExponent)),
            (TextStyle != NumberTextStyle.Plain, nameof(TextStyle)),
            (ExportFormat is not null, nameof(ExportFormat)),
            (NumericRounding is not null || TimeRounding is not null, "Rounding"),
            (AllowNonFiniteValues, "NonFiniteValues"),
            (TimeFormat is not null, "Format"),
            (FileTimeZone is not null, nameof(FileTimeZone)),
            (AmbiguousTimeResolution is not null, "ResolveAmbiguousTime"),
            (ShiftInvalidTime, nameof(ShiftInvalidTime)),
            (ExcelAsUtc, nameof(ExcelAsUtc)),
            (AllowExcelSerialNumbers, nameof(AllowExcelSerialNumbers)),
            (ExcelDurationUnit != ExcelDurationUnit.Days, "ExcelDuration"),
            (GuidInputFormats is not null, "AcceptGuidFormats"),
            (GuidOutputFormat is not null, "GuidExportFormat"),
        ];

        return settings
            .Where(setting => setting.IsSet)
            .Select(setting => setting.Name);
    }

    /// <summary>
    /// A null marker that the trimming of this call reads as an empty value; Build checked a fixed marker for Trim().
    /// The import and the export of a call check the same way.
    /// </summary>
    public string? BlankNullMarker(
        ConversionContext context
    )
    {
        var checkedSettings = CheckedConversionSettings();
        var baseSettings = Conversion?.BaseSettings ?? this;

        // WHY: An empty translation is no marker with or without trimming; it is found before the first access.
        foreach (var key in new[] { EmptyTranslationKey, baseSettings.EmptyTranslationKey })
        {
            if (key is not null
                && context.Translations is { } given
                && given.TryGetValue(key, out var marker)
                && marker.Length == 0)
            {
                return "The translation of the null marker is empty.";
            }
        }

        if (!context.TrimFor(checkedSettings))
        {
            return null;
        }

        if (checkedSettings.Trim != true
            && (IsBlank(NullText) || IsBlank(baseSettings.NullText)))
        {
            return "NullText is empty after trimming; a trimming field reads it as an empty value.";
        }

        foreach (var key in new[] { EmptyTranslationKey, baseSettings.EmptyTranslationKey })
        {
            if (key is not null
                && context.Translations is { } translations
                && translations.TryGetValue(key, out var text)
                && string.IsNullOrWhiteSpace(text))
            {
                return "The translation of the null marker is empty after trimming.";
            }
        }

        return null;

        static bool IsBlank(
            string? text
        ) => text is { Length: > 0 } && string.IsNullOrWhiteSpace(text);
    }

    public FieldSettings Clone()
    {
        var copy = (FieldSettings)MemberwiseClone();
        copy.Validators = [.. Validators];
        copy.ExportAliases = [.. ExportAliases];
        copy.ConfigurationProblems = [.. ConfigurationProblems];
        if (Conversion is not null)
        {
            copy.Conversion = Conversion.WithBaseSettings(Conversion.BaseSettings.Clone());
        }

        return copy;
    }
}
