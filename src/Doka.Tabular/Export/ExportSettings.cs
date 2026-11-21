namespace Doka.Tabular;

/// <summary>Resolved per-call export settings shared by all formats.</summary>
internal sealed class ExportSettings
{
    public required bool WriteHeader { get; init; }

    public required CultureInfo Culture { get; init; }

    public required IReadOnlyDictionary<string, string>? Translations { get; init; }

    public required MissingTranslationBehavior MissingTranslation { get; init; }

    public required IReadOnlyList<ColumnKey>? ColumnKeys { get; init; }
}

internal sealed record ExportRole(
    MapModel Model,
    EmbeddingSettings? Embedding,
    Func<object, object?> Accessor
);

/// <summary>What one exported source object provides: the roles and how to reach their records.</summary>

// ReSharper disable once UnusedTypeParameter
// WHY: The type parameter ties a shape to the plan and source of the same record type at compile time.
internal sealed class ExportShape<TSource>
{
    private ExportShape(
        IReadOnlyList<ExportRole> roles
    )
    {
        Roles = roles;
    }

    public IReadOnlyList<ExportRole> Roles { get; }

    public static ExportShape<T> ForMap<T>(
        TabularMap<T> map
    ) => new([new ExportRole(map.Model, null, source => source)]);

    public static ExportShape<TabularRow> ForRowMap(
        RowMap map
    ) => new(map
        .Roles
        .Select(role
            => new ExportRole(role.Model, role.Embedding, source => ((TabularRow)source).Items[role.Ordinal]))
        .ToArray());

    public static ExportShape<T> ForRowExport<T>(
        RowExportMap<T> map
    ) => new(map
        .Map
        .Roles
        .Where(role => map.Bindings.ContainsKey(role.Ordinal))
        .Select(role => new ExportRole(role.Model,
            role.Embedding,
            source => map
                .Bindings[role.Ordinal]((T)source)))
        .ToArray());
}

/// <summary>One output column of a call.</summary>
internal sealed class ExportColumn
{
    public ExportColumn(
        int position,
        FlatField? field,
        ExportRole? role,
        ValueConverter? converter,
        string? header,
        FieldFormatter? formatter
    )
    {
        Position = position;
        Field = field;
        Role = role;
        Converter = converter;
        Header = header;
        Formatter = formatter;
    }

    /// <summary>Gets the typed formatter of the field; <see langword="null" /> for a gap.</summary>
    public FieldFormatter? Formatter { get; }

    /// <summary>Gets the zero-based output position.</summary>
    public int Position { get; }

    /// <summary>Gets the field; <see langword="null" /> for a gap of the full positional export.</summary>
    public FlatField? Field { get; }

    public ExportRole? Role { get; }

    public ValueConverter? Converter { get; }

    public string? Header { get; }

    public bool IsGap => Field is null;
}
