using System.Numerics;

namespace Doka.Tabular;

/// <summary>
/// Enum conversion for one field and one call, built with the call's translations. Values are handled as their
/// raw bits; <see cref="EnumConverter{TEnum}" /> adapts the engine to the enum type without boxing.
/// </summary>
internal sealed partial class EnumConverter
{
    private readonly HashSet<ulong> _declared;
    private readonly ulong[] _descending;
    private readonly Dictionary<ulong, string> _fixedTexts = [];
    private readonly Dictionary<ulong, string> _keys = [];
    private readonly ulong[] _memberRaws;
    private readonly char _separator;
    private readonly EnumShape _shape;
    private readonly HashSet<ulong> _singles;
    private readonly Dictionary<string, ulong> _texts = new(StringComparer.OrdinalIgnoreCase);

    private EnumConverter(
        EnumShape shape,
        char separator
    )
    {
        _shape = shape;
        _separator = separator;

        // WHY: Fluent materialization stays explicit as ToArray or ToList instead of a spread.
        // ReSharper disable once UseCollectionExpression
        _memberRaws = shape
            .Members
            .Select(member => member.Raw)
            .ToArray();

        _declared = [.. _memberRaws];

        // WHY: Fluent materialization stays explicit as ToArray or ToList instead of a spread.
        // ReSharper disable once UseCollectionExpression
        _singles = _memberRaws
            .Where(raw => raw != 0 && BitOperations.IsPow2(raw))
            .ToHashSet();

        // WHY: Fluent materialization stays explicit as ToArray or ToList instead of a spread.
        // ReSharper disable once UseCollectionExpression
        _descending = _memberRaws
            .Distinct()
            .OrderByDescending(raw => raw)
            .ToArray();
    }

    private Dictionary<string, ulong>.AlternateLookup<ReadOnlySpan<char>> Lookup
        => _texts.GetAlternateLookup<ReadOnlySpan<char>>();

    /// <summary>Structural checks of <c>Build()</c>, independent of a call.</summary>
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public static IEnumerable<string> ValidateStructure(
        Type enumType,
        FieldSettings settings,
        bool isExported
    )
    {
        var shape = EnumShape.Of(enumType);
        var separator = settings.FlagsSeparator ?? ',';
        return SettingsProblems(shape, settings)
            .Concat(shape.Members.SelectMany(member => AttributeProblems(shape, member, separator)))

            // WHY: The import always accepts the C# names; a name with the separator would split into unknown names.
            .Concat(
                shape.Members.SelectMany(member => TextProblems("name", shape, member.Name, member.Name, separator)))
            .Concat(Collisions(shape, settings.AllowNumericInput, settings.Trim == true, translations: null))
            .Concat(AliasProblems(shape, settings, isExported))
            .Concat(settings.Trim == true
                ? BlankAttributes(shape)
                : []);
    }

    /// <summary>
    /// Attribute texts of white space only: a trimming field reads them as an empty value, which never names a value,
    /// so the own output would not read back.
    /// </summary>
    private static IEnumerable<string> BlankAttributes(
        EnumShape shape
    ) => shape
        .Members
        .Where(member => member.AttributeText is { Length: > 0 } text && string.IsNullOrWhiteSpace(text))
        .Select(member => $"The attribute text of {shape.Type.Name}.{member.Name} is empty after trimming.");

    /// <summary>Translations of white space only, which a trimming field reads as an empty value.</summary>
    private static IEnumerable<string> BlankTranslations(
        EnumShape shape,
        Dictionary<ulong, string> translations
    ) => translations
        .Where(entry => string.IsNullOrWhiteSpace(entry.Value))
        .Select(_ => $"The translation of a value of {shape.Type.Name} is empty after trimming.");

    private static IEnumerable<string> SettingsProblems(
        EnumShape shape,
        FieldSettings settings
    )
    {
        if (settings.FlagsSeparator is '"' or '+' or '-'
            || (settings.FlagsSeparator is { } chosen
                && (char.IsWhiteSpace(chosen)
                    || char.IsControl(chosen)
                    || char.IsSurrogate(chosen)
                    || char.IsDigit(chosen))))
        {
            yield return
                "The flags separator must not be a quote, white space, control character, surrogate, digit, plus, or minus.";
        }

        if (settings.FlagsSeparator is not null
            && !shape.IsFlags)
        {
            yield return "FlagsSeparator applies to flags enums only.";
        }

        if (settings.NumericRounding is not null)
        {
            yield return "Enums do not accept rounding.";
        }
    }

    private static IEnumerable<string> AttributeProblems(
        EnumShape shape,
        EnumMember member,
        char separator
    ) => member.AttributeText switch
    {
        { Length: 0 } => [$"The attribute text of {shape.Type.Name}.{member.Name} is empty."],
        { } text => TextProblems("attribute text", shape, member.Name, text, separator),
        null => [],
    };

    /// <summary>Flags texts must not begin or end with white space or contain the separator.</summary>
    private static IEnumerable<string> TextProblems(
        string label,
        EnumShape shape,
        string memberName,
        string text,
        char separator
    )
    {
        if (!shape.IsFlags)
        {
            yield break;
        }

        if (text.Trim(' ', '\t')
                .Length
            != text.Length)
        {
            yield return $"The {label} of {shape.Type.Name}.{memberName} begins or ends with white space.";
        }

        if (text.Contains(separator, StringComparison.Ordinal))
        {
            yield return $"The {label} of {shape.Type.Name}.{memberName} contains the flags separator.";
        }
    }

    /// <summary>Aliases with different texts need one chosen export alias; each value takes at most one.</summary>
    private static IEnumerable<string> AliasProblems(
        EnumShape shape,
        FieldSettings settings,
        bool isExported
    )
    {
        if (isExported
            && settings.EnumTranslationKey is null)
        {
            foreach (var group in UndecidedAliases(shape, settings))
            {
                yield return
                    $"The aliases {string.Join(", ", group.Select(member => member.Name))} of {shape.Type.Name} have different texts; choose one with ExportAlias or use TranslationKeys.";
            }
        }

        foreach (var unknown in settings.ExportAliases.Where(alias => RawOf(shape, alias) is null))
        {
            yield return $"ExportAlias names {unknown}, which is no member of {shape.Type.Name}.";
        }

        foreach (var group in settings
            .ExportAliases
            .Where(alias => RawOf(shape, alias) is not null)
            .GroupBy(alias => RawOf(shape, alias)!.Value)
            .Where(group => group.Count() > 1))
        {
            yield return
                $"ExportAlias is set more than once for the value {shape.ToNumber(group.Key)} of {shape.Type.Name}.";
        }
    }

    /// <summary>The aliases of each value whose fixed texts differ while no export alias is chosen for it.</summary>
    private static IEnumerable<IGrouping<ulong, EnumMember>> UndecidedAliases(
        EnumShape shape,
        FieldSettings settings
    ) => settings.ExportAsNumber
        ? []
        : shape
            .Members
            .GroupBy(member => member.Raw)
            .Where(group => group
                    .Select(member => member.FixedText(settings.ExportMemberName))
                    .Distinct(StringComparer.Ordinal)
                    .Skip(1)
                    .Any()
                && settings.ExportAliases.All(alias => RawOf(shape, alias) != group.Key));

    /// <summary>The raw value of the member with this C# name; <see langword="null" /> when there is none.</summary>
    private static ulong? RawOf(
        EnumShape shape,
        string memberName
    ) => shape
        .Members
        .FirstOrDefault(member => string.Equals(member.Name, memberName, StringComparison.Ordinal))
        ?.Raw;

    /// <summary>The typed converter of an enum field for one call.</summary>
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    [RequiresDynamicCode("The converter is a generic type closed over the enum type at run time.")]
    public static ValueConverter Create(
        Type enumType,
        FieldSettings settings,
        ConversionContext context,
        bool forExport,
        List<string> problems
    )
    {
        EnumConverter engine;
        try
        {
            engine = CreateEngine(enumType, settings, context, forExport, problems);
        }
        catch (Exception exception) when (Callbacks.Failed(exception, CancellationToken.None))
        {
            // WHY: The translation key function runs when the call starts, before the file is accessed; like a
            // failing factory setup it is a configuration error, and the converter without texts is never used.
            problems.Add($"The translation key function of {enumType.Name} threw an exception.");
            engine = new EnumConverter(EnumShape.Of(enumType), settings.FlagsSeparator ?? ',');
        }

        return (ValueConverter)Activator.CreateInstance(typeof(EnumConverter<>).MakeGenericType(enumType), engine)!;
    }

    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    private static EnumConverter CreateEngine(
        Type enumType,
        FieldSettings settings,
        ConversionContext context,
        bool forExport,
        List<string> problems
    )
    {
        var shape = EnumShape.Of(enumType);
        var separator = settings.FlagsSeparator ?? ',';
        var converter = new EnumConverter(shape, separator);
        Dictionary<ulong, string>? translated = null;
        if (settings.EnumTranslationKey is { } keyOf
            && context.Translations is { } translations)
        {
            translated = Translate(shape, keyOf, translations, separator, problems);
        }

        // WHY: The call decides the trimming; labels that collide only after trimming are found here.
        if (translated is not null
            || (context.TrimFor(settings) && settings.Trim != true))
        {
            problems.AddRange(Collisions(shape, settings.AllowNumericInput, context.TrimFor(settings), translated));
        }

        // WHY: With the trimming of the call, labels of white space only vanish; Build checked them for Trim().
        if (context.TrimFor(settings))
        {
            if (settings.Trim != true)
            {
                problems.AddRange(BlankAttributes(shape));
            }

            if (translated is not null)
            {
                problems.AddRange(BlankTranslations(shape, translated));
            }
        }

        // WHY: Without its translation a value with differing alias texts falls back to a fixed text, and no
        // fixed text is chosen.
        if (forExport
            && settings.EnumTranslationKey is not null)
        {
            problems.AddRange(UndecidedAliases(shape, settings)
                .Where(group => translated?.ContainsKey(group.Key) != true)
                .Select(group
                    => $"The aliases {string.Join(", ", group.Select(member => member.Name))} of {shape.Type.Name} have different texts and no translation in this call; choose one with ExportAlias."));
        }

        // WHY: A trimmed input is compared with trimmed labels, so a padded label written by the export reads back.
        var trims = context.TrimFor(settings);

        // WHY: Indexed access reads the list without an enumerator through the interface.
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var memberIndex = 0; memberIndex < shape.Members.Count; memberIndex++)
        {
            var member = shape.Members[memberIndex];
            converter._texts.TryAdd(member.Name, member.Raw);
            if (member.AttributeText is { Length: > 0 } attribute)
            {
                converter._texts.TryAdd(LabelKey(attribute, trims), member.Raw);
            }
        }

        foreach (var (raw, text) in translated ?? new Dictionary<ulong, string>())
        {
            converter._texts.TryAdd(LabelKey(text, trims), raw);
        }

        converter.PrepareTexts(settings);

        return converter;
    }

    private static string LabelKey(
        string label,
        bool trims
    ) => trims
        ? label.Trim()
        : label;

    /// <summary>The fixed export text and the translation key of every declared value and of 0, once per call.</summary>
    private void PrepareTexts(
        FieldSettings settings
    )
    {
        // WHY: Indexed access reads the list without an enumerator through the interface.
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var memberIndex = 0; memberIndex < _shape.Members.Count; memberIndex++)
        {
            var member = _shape.Members[memberIndex];
            _fixedTexts.TryAdd(member.Raw, member.FixedText(settings.ExportMemberName));
        }

        // WHY: Lists are read by index throughout Tabular; foreach is kept for arrays, spans, and sequences.
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var aliasIndex = 0; aliasIndex < settings.ExportAliases.Count; aliasIndex++)
        {
            var alias = settings.ExportAliases[aliasIndex];
            var member = _shape.Members.First(candidate
                => string.Equals(candidate.Name, alias, StringComparison.Ordinal));

            _fixedTexts[member.Raw] = member.FixedText(settings.ExportMemberName);
        }

        if (settings.EnumTranslationKey is not { } keyOf)
        {
            return;
        }

        // WHY: Only a flags enum writes 0 without a zero member, through its own key; a normal enum has no such value,
        // so the key function is not asked for it.
        foreach (var raw in _shape.IsFlags
            ? _memberRaws.Append(0UL)
            : _memberRaws)
        {
            _keys.TryAdd(raw, keyOf(_shape.FromRaw(raw)));
        }
    }

    /// <summary>The translated text per value; aliases share the first translated text.</summary>
    private static Dictionary<ulong, string> Translate(
        EnumShape shape,
        Func<object, string> keyOf,
        IReadOnlyDictionary<string, string> translations,
        char separator,
        List<string> problems
    )
    {
        var map = new Dictionary<ulong, string>();

        // WHY: Indexed access reads the list without an enumerator through the interface.
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var memberIndex = 0; memberIndex < shape.Members.Count; memberIndex++)
        {
            var member = shape.Members[memberIndex];
            if (!translations.TryGetValue(keyOf(shape.FromRaw(member.Raw)), out var text))
            {
                continue;
            }

            if (text.Length == 0)
            {
                problems.Add($"The translation of {shape.Type.Name}.{member.Name} is empty.");
                continue;
            }

            problems.AddRange(TextProblems("translation", shape, member.Name, text, separator));

            // Aliases share one value; the first translated text is the export text.
            map.TryAdd(member.Raw, text);
        }

        // WHY: The export writes 0 of a flags enum without a zero member through its own key, so the import must
        // know that text as well.
        if (shape.IsFlags
            && !map.ContainsKey(0)
            && translations.TryGetValue(keyOf(shape.FromRaw(0)), out var none))
        {
            if (none.Length == 0)
            {
                problems.Add($"The translation of {shape.Type.Name} 0 is empty.");
            }
            else
            {
                problems.AddRange(TextProblems("translation", shape, "0", none, separator));
                map.TryAdd(0, none);
            }
        }

        return map;
    }

    /// <summary>The message of a text that denotes two values; it names both, whichever way the text collides.</summary>
    private static string Collision(
        EnumShape shape,
        string text,
        string first,
        string second
    ) => $"The text {text} denotes different values of {shape.Type.Name}: {first} and {second}.";

    /// <summary>The value a number text denotes with numeric input: its member, or the number when no member has it.</summary>
    private static string NumberValue(
        EnumShape shape,
        BigInteger number
    )
    {
        foreach (var member in shape.Members)
        {
            if (shape.ToNumber(member.Raw) == number)
            {
                return $"{member.Name} ({number.ToString(CultureInfo.InvariantCulture)})";
            }
        }

        return $"the number {number.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>Texts that denote different values, compared as the reader sees them after trimming.</summary>
    private static IEnumerable<string> Collisions(
        EnumShape shape,
        bool numeric,
        bool trims,
        IReadOnlyDictionary<ulong, string>? translations
    )
    {
        var texts = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);
        var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (label, raw) in Labels())
        {
            var text = trims
                ? label.Trim()
                : label;

            if (texts.TryGetValue(text, out var existing)
                && existing != raw
                && reported.Add(text))
            {
                yield return Collision(shape, text, $"{shape.FromRaw(existing)}", $"{shape.FromRaw(raw)}");
            }

            // WHY: With numeric input every number text is read as a number first, also one with a sign or
            // leading zeros; a label in such a form must denote that very number.
            if (numeric
                && IsNumericText(text)
                && BigInteger.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture) is var number
                && number != shape.ToNumber(raw)
                && reported.Add(text))
            {
                yield return Collision(shape, text, NumberValue(shape, number), $"{shape.FromRaw(raw)}");
            }

            texts.TryAdd(text, raw);
        }

        yield break;

        IEnumerable<(string Text, ulong Raw)> Labels()
        {
            // WHY: Indexed access reads the list without an enumerator through the interface.
            // ReSharper disable once ForCanBeConvertedToForeach
            for (var memberIndex = 0; memberIndex < shape.Members.Count; memberIndex++)
            {
                var member = shape.Members[memberIndex];
                yield return (member.Name, member.Raw);
                if (member.AttributeText is { Length: > 0 } attribute)
                {
                    yield return (attribute, member.Raw);
                }
            }

            if (translations is not null)
            {
                foreach (var (raw, text) in translations)
                {
                    yield return (text, raw);
                }
            }

            if (numeric)
            {
                // WHY: Indexed access reads the list without an enumerator through the interface.
                // ReSharper disable once ForCanBeConvertedToForeach
                for (var memberIndex = 0; memberIndex < shape.Members.Count; memberIndex++)
                {
                    var member = shape.Members[memberIndex];
                    yield return (shape
                        .ToNumber(member.Raw)
                        .ToString(CultureInfo.InvariantCulture), member.Raw);
                }
            }
        }
    }
}
