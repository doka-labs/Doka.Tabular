namespace Doka.Tabular;

/// <summary>Header text sources of one field: an override, or prefixes plus the field's own header.</summary>
internal sealed record HeaderSpec(
    string? Override,
    IReadOnlyList<HeaderPrefix> Prefixes,
    string? Fixed,
    string? TranslationKey
);

internal sealed record HeaderPrefix(
    string? Fixed,
    string? TranslationKey
);

/// <summary>One leaf column after applying all inclusions: effective index, key, path, and header.</summary>
internal sealed class FlatField
{
    public FlatField(
        TabularPropertyPath path,
        ColumnKey key,
        int index,
        FieldSettings settings,
        FieldCodec codec,
        FieldDirection direction,
        HeaderSpec header
    )
    {
        Path = path;
        Key = key;
        Index = index;
        Settings = settings;
        Codec = codec;
        Direction = direction;
        Header = header;
    }

    public TabularPropertyPath Path { get; }

    public ColumnKey Key { get; }

    public int Index { get; }

    public FieldSettings Settings { get; }

    /// <summary>Gets the typed stages of the field value.</summary>
    public FieldCodec Codec { get; }

    public Type ValueType => Codec.ValueType;

    private FieldDirection Direction { get; }

    public HeaderSpec Header { get; }

    public bool IsImported => Direction is FieldDirection.Both or FieldDirection.ImportOnly;

    public bool IsExported => Direction is FieldDirection.Both or FieldDirection.ExportOnly;
}

internal abstract record NodeMember(MapMember Member);

internal sealed record ColumnNode(
    ColumnMember Column,
    FlatField Field
) : NodeMember(Column);

internal sealed record ComposeNode(
    ComposeMember Compose,
    IReadOnlyList<FlatField> Parts
) : NodeMember(Compose);

internal sealed record NestedNode : NodeMember
{
    public NestedNode(
        NestedMember nested,
        MapNode node
    )
        : base(nested)
    {
        Node = node;
    }

    public MapNode Node { get; }
}

/// <summary>One map inside the flattened tree, with the path of its property.</summary>
internal sealed record MapNode(
    MapModel Model,
    TabularPropertyPath? Path,
    IReadOnlyList<NodeMember> Members
);

internal sealed class EmbeddingContext
{
    private EmbeddingContext(
        int offset,
        IReadOnlyList<ColumnKey> keyPrefixes,
        IReadOnlyList<HeaderPrefix> headerPrefixes,
        bool importOnly,
        IReadOnlyList<(string RelativePath, EmbeddingSettings Settings)> overrides
    )
    {
        Offset = offset;
        KeyPrefixes = keyPrefixes;
        HeaderPrefixes = headerPrefixes;
        ImportOnly = importOnly;
        Overrides = overrides;
    }

    public static EmbeddingContext Root { get; } = new(0, [], [], false, []);

    public int Offset { get; }

    public IReadOnlyList<ColumnKey> KeyPrefixes { get; }

    public IReadOnlyList<HeaderPrefix> HeaderPrefixes { get; }

    public bool ImportOnly { get; }

    /// <summary>Embedding settings of enclosing inclusions with the path of this map relative to each of them.</summary>
    public IReadOnlyList<(string RelativePath, EmbeddingSettings Settings)> Overrides { get; }

    public EmbeddingContext Enter(
        EmbeddingSettings embedding,
        string memberName
    )
    {
        var keyPrefixes = embedding.KeyPrefix is null
            ? KeyPrefixes
            : [.. KeyPrefixes, embedding.KeyPrefix];

        var headerPrefixes = embedding.HeaderPrefix is null && embedding.HeaderPrefixTranslationKey is null
            ? HeaderPrefixes
            : [.. HeaderPrefixes, new HeaderPrefix(embedding.HeaderPrefix, embedding.HeaderPrefixTranslationKey)];

        var overrides = Overrides
            .Select(entry => (Join(entry.RelativePath, memberName), entry.Settings))
            .ToList();

        overrides.Add((string.Empty, embedding));

        return new EmbeddingContext(Offset + embedding.StartIndex,
            keyPrefixes,
            headerPrefixes,
            ImportOnly || embedding.IsImportOnly,
            overrides);
    }

    public static string Join(
        string left,
        string right
    ) => left.Length == 0
        ? right
        : $"{left}.{right}";
}

internal static class MapFlattening
{
    public static MapNode Flatten(
        MapModel model,
        TabularPropertyPath? path,
        EmbeddingContext context,
        List<FlatField> fields
    )
    {
        var members = new List<NodeMember>();

        // WHY: Indexed access reads the list without an enumerator through the interface.
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var memberIndex = 0; memberIndex < model.Members.Count; memberIndex++)
        {
            var member = model.Members[memberIndex];
            var memberPath = path is null
                ? TabularPropertyPath.Of(member.Name)
                : path.Append(member.Name);

            switch (member)
            {
                case ColumnMember column:
                    members.Add(new ColumnNode(column,
                        Leaf(fields,
                            memberPath,
                            member.Name,
                            column.Key,
                            column.Index,
                            column.Settings,
                            column.Codec,
                            context)));

                    break;
                case ComposeMember compose:
                    var parts = compose
                        .Parts
                        .Select(part => Leaf(fields,
                            memberPath,
                            part.Key.Value,
                            part.Key,
                            part.Index,
                            part.Settings,
                            part.Codec,
                            context))
                        .ToList();

                    members.Add(new ComposeNode(compose, parts));
                    break;
                case NestedMember nested:
                    var inner = context.Enter(nested.Embedding, member.Name);
                    members.Add(new NestedNode(nested, Flatten(nested.Map, memberPath, inner, fields)));
                    break;
            }
        }

        return new MapNode(model, path, members);
    }

    private static FlatField Leaf(
        List<FlatField> fields,
        TabularPropertyPath path,
        string localName,
        ColumnKey key,
        int declaredIndex,
        FieldSettings settings,
        FieldCodec codec,
        EmbeddingContext context
    )
    {
        ColumnKey? keyOverride = null;
        string? headerOverride = null;
        foreach (var (relativePath, embedding) in context.Overrides)
        {
            var fieldPath = EmbeddingContext.Join(relativePath, localName);
            if (embedding.ExportKeys.TryGetValue(fieldPath, out var exportKey))
            {
                keyOverride = exportKey;
            }

            if (embedding.Headers.TryGetValue(fieldPath, out var header))
            {
                headerOverride = header;
            }
        }

        var effectiveKey = keyOverride
            ?? context
                .KeyPrefixes
                .Reverse()
                .Aggregate(key, (current, prefix) => ColumnKey.Combine(prefix, current));

        var direction = (context.ImportOnly, settings.Direction) switch
        {
            (false, var declared) => declared,
            (true, FieldDirection.ExportOnly) => FieldDirection.None,
            (true, FieldDirection.Both or FieldDirection.ImportOnly or FieldDirection.None) =>
                FieldDirection.ImportOnly,
            (true, var undefined) => throw new UnreachableException($"The field direction {undefined} is not defined."),
        };

        var flat = new FlatField(
            path,
            effectiveKey,
            context.Offset + declaredIndex,
            settings,
            codec,
            direction,
            new HeaderSpec(headerOverride, context.HeaderPrefixes, settings.Header, settings.HeaderTranslationKey));

        fields.Add(flat);

        return flat;
    }
}
