namespace Doka.Tabular.Excel;

/// <summary>Checks a sheet layout before the first write; everything detectable in advance fails here.</summary>
internal static class ExcelLayoutValidation
{
    public static ExcelSheetPlan? Plan(
        TabularSheetName name,
        ExcelLayoutOptions layout,
        IReadOnlyList<ExportColumn> columns,
        IReadOnlySet<ColumnKey> exportKeys,
        bool writeHeader,
        ExcelTableNames tableNames,
        List<TabularError> errors
    )
    {
        var location = new TabularErrorLocation { SheetName = name };
        var before = errors.Count;
        CheckPosition(layout, columns, location, errors);
        CheckOptions(layout, columns, writeHeader, location, errors);
        CheckColumnLayouts(layout, columns, exportKeys, name, errors);
        CheckHeaderCells(columns, writeHeader, location, errors);
        ExcelTableName? tableName = null;
        if (layout.Mode == ExcelLayoutMode.Table)
        {
            CheckTable(columns, writeHeader, location, errors);
            tableName = tableNames.Assign(layout.TableName, location, errors);
        }

        return errors.Count > before
            ? null
            : new ExcelSheetPlan
            {
                Name = name,
                Layout = layout,
                Columns = columns,

                // WHY: Without columns no header row is written, so nothing may be frozen, filtered, or counted for it.
                WriteHeader = writeHeader && columns.Count > 0,
                TableName = tableName,
            };
    }

    /// <summary>The output must start inside the sheet and fit into its columns and rows.</summary>
    private static void CheckPosition(
        ExcelLayoutOptions layout,
        IReadOnlyList<ExportColumn> columns,
        TabularErrorLocation location,
        List<TabularError> errors
    )
    {
        if (layout.StartRow < 1
            || layout.StartColumn < 1)
        {
            errors.Add(TabularError.Configuration("StartRow and StartColumn start at 1.", location));
        }

        // WHY: The start cell lies inside the sheet also when no column or no header row is written there.
        if (layout.StartColumn > ExcelWorkbookWriter.MaxColumns
            || layout.StartColumn - 1L + columns.Count > ExcelWorkbookWriter.MaxColumns)
        {
            errors.Add(
                TabularError.Create(
                    TabularErrorCodes.Schema.LimitExceeded,
                    TabularErrorKind.Configuration,
                    "The output exceeds 16,384 columns.",
                    location));
        }

        if (layout.StartRow > ExcelWorkbookWriter.MaxRows)
        {
            errors.Add(
                TabularError.Create(
                    TabularErrorCodes.Schema.LimitExceeded,
                    TabularErrorKind.Configuration,
                    "The start row leaves no room in the sheet.",
                    location));
        }
    }

    private static void CheckOptions(
        ExcelLayoutOptions layout,
        IReadOnlyList<ExportColumn> columns,
        bool writeHeader,
        TabularErrorLocation location,
        List<TabularError> errors
    )
    {
        var problems = new[]
        {
            layout is { Mode: ExcelLayoutMode.Range }
            && (layout.TableName is not null || layout.TableStyle is not null || layout.BandedRows is not null)
                ? "TableName, TableStyle, and BandedRows are allowed in table mode only."
                : null,
            (layout.Filter == ExcelHeaderFeature.Enabled || layout.FreezeHeader == ExcelHeaderFeature.Enabled)
            && !writeHeader
                ? "An enabled filter or frozen header requires a header row."
                : null,
            layout.FrozenColumns < 0 || layout.FrozenColumns > columns.Count
                ? "FrozenColumns must be between 0 and the output width."
                : null,
            !double.IsFinite(layout.MaximumAutoWidth) || layout.MaximumAutoWidth <= 0 || layout.MaximumAutoWidth > 255
                ? "MaximumAutoWidth must be positive and at most 255, the widest Excel column."
                : null,
            layout.HeaderHeight is { } height && (!double.IsFinite(height) || height <= 0 || height > 409)
                ? "HeaderHeight must be positive and at most 409 points."
                : null,
        };

        errors.AddRange(
            problems
                .OfType<string>()
                .Select(problem => TabularError.Configuration(problem, location)));
    }

    /// <summary>
    /// Every column layout must name an export key of the map and have a valid width; a known key whose column is not
    /// selected may keep its settings.
    /// </summary>
    private static void CheckColumnLayouts(
        ExcelLayoutOptions layout,
        IReadOnlyList<ExportColumn> columns,
        IReadOnlySet<ColumnKey> exportKeys,
        TabularSheetName name,
        List<TabularError> errors
    )
    {
        foreach (var (key, column) in layout.Columns ?? new Dictionary<ColumnKey, ExcelColumnLayout>())
        {
            var location = new TabularErrorLocation
            {
                SheetName = name,
                ColumnKey = key,
            };

            if (columns.All(entry => entry.Field?.Key != key)
                && !exportKeys.Contains(key))
            {
                errors.Add(TabularError.Configuration($"The layout refers to the unknown key {key}.", location));
            }

            if (column.Width is { } width
                && (!double.IsFinite(width) || width <= 0 || width > 255))
            {
                errors.Add(TabularError.Configuration("A column width must be positive and at most 255.", location));
            }
        }
    }

    /// <summary>A table needs a header row, no gaps, and unique non-empty headers of at most 255 characters.</summary>
    /// <summary>A header is a cell like any other: at most 32,767 characters and 253 line breaks.</summary>
    private static void CheckHeaderCells(
        IReadOnlyList<ExportColumn> columns,
        bool writeHeader,
        TabularErrorLocation location,
        List<TabularError> errors
    )
    {
        if (!writeHeader)
        {
            return;
        }

        // WHY: Indexed access reads the list without an enumerator through the interface.
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var position = 0; position < columns.Count; position++)
        {
            if (columns[position].Header is { } header
                && (header.Length > ExcelWorkbookWriter.MaxCellCharacters
                    || header
                        .AsSpan()
                        .Count('\n')
                    > ExcelWorkbookWriter.MaxLineBreaks))
            {
                errors.Add(
                    TabularError.Create(
                        TabularErrorCodes.Schema.LimitExceeded,
                        TabularErrorKind.Configuration,
                        "A header exceeds 32,767 characters or 253 line breaks.",
                        new TabularErrorLocation
                        {
                            SheetName = location.SheetName,
                            ColumnIndex = columns[position].Position,
                        }));
            }
        }
    }

    private static void CheckTable(
        IReadOnlyList<ExportColumn> columns,
        bool writeHeader,
        TabularErrorLocation location,
        List<TabularError> errors
    )
    {
        var headers = columns
            .Where(column => !column.IsGap)
            .Select(column => column.Header)
            .ToList();

        var problems = new[]
        {
            writeHeader
                ? null
                : "Table mode requires a header row.",
            columns.Any(column => column.IsGap)
                ? "Table mode requires an output without gaps."
                : null,
            headers.Any(string.IsNullOrEmpty)
                ? "Table mode requires a non-empty header for every column."
                : null,
            headers.Any(header => header is { Length: > 255 })
                ? "Table headers have at most 255 characters."
                : null,
            headers
                .Where(header => !string.IsNullOrEmpty(header))
                .GroupBy(header => header, StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() > 1)
                ? "Table headers must be unique (ignoring case)."
                : null,
        };

        errors.AddRange(
            problems
                .OfType<string>()
                .Select(problem => TabularError.Configuration(problem, location)));
    }
}

/// <summary>Assigns table names workbook-wide: explicit names must be unique, automatic ones count up.</summary>
internal sealed class ExcelTableNames
{
    private readonly HashSet<ExcelTableName> _used = [];
    private int _automatic;

    public ExcelTableName Assign(
        ExcelTableName? requested,
        TabularErrorLocation location,
        List<TabularError> errors
    )
    {
        if (requested is not null)
        {
            if (!_used.Add(requested))
            {
                errors.Add(
                    TabularError.Configuration(
                        $"The table name {requested} is used more than once in the workbook.",
                        location));
            }

            return requested;
        }

        while (true)
        {
            _automatic++;
            var candidate = ExcelTableName.Automatic(
                _automatic == 1
                    ? "TabularExport"
                    : $"TabularExport_{_automatic.ToString(CultureInfo.InvariantCulture)}");

            if (_used.Add(candidate))
            {
                return candidate;
            }
        }
    }
}
