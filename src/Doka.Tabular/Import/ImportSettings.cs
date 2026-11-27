using System.Text;

namespace Doka.Tabular;

/// <summary>Resolved per-call import settings shared by all formats.</summary>
internal sealed class ImportSettings
{
    public required ImportStrategy Strategy { get; init; }

    public required ColumnBinding Binding { get; init; }

    public required bool TrimFields { get; init; }

    public required CultureInfo Culture { get; init; }

    public required IReadOnlyDictionary<string, string>? Translations { get; init; }

    public required MissingTranslationBehavior MissingTranslation { get; init; }

    public required Func<string, string>? NormalizeHeader { get; init; }

    public required int? MaxErrorDetails { get; init; }

    public required TabularSourceKey? SourceKey { get; init; }

    /// <summary>Gets the source keys of the write targets; applied together with <see cref="SourceKey" />.</summary>
    public IReadOnlyList<TabularSourceKey> TargetKeys { get; init; } = [];

    public IEnumerable<TabularSourceKey> AllKeys => SourceKey is null
        ? TargetKeys
        : TargetKeys.Prepend(SourceKey);
}

internal static class HeaderText
{
    /// <summary>Builds the effective header of a field for this call; export and header-name import use the same text.</summary>
    public static bool TryResolve(
        FlatField field,
        ConversionContext context,
        out string? header,
        out TabularError? error
    )
    {
        error = null;
        header = null;
        if (field.Header.Override is { } fixedOverride)
        {
            header = fixedOverride;

            return true;
        }

        var builder = new StringBuilder();
        foreach (var prefix in field.Header.Prefixes)
        {
            if (!TryText(prefix.TranslationKey, prefix.Fixed, field, context, out var text, out error))
            {
                return false;
            }

            builder.Append(text);
        }

        if (field.Header.TranslationKey is null
            && field.Header.Fixed is null)
        {
            header = null;

            return true;
        }

        if (!TryText(field.Header.TranslationKey, field.Header.Fixed, field, context, out var own, out error))
        {
            return false;
        }

        header = builder
            .Append(own)
            .ToString();

        return true;
    }

    /// <summary>
    /// Gets the header that does not depend on a call: an override or fixed texts only; <see langword="null" /> when
    /// a translation takes part or the field has no header.
    /// </summary>
    public static string? Fixed(
        FlatField field
    )
    {
        if (field.Header.Override is { } fixedOverride)
        {
            return fixedOverride;
        }

        if (field.Header.Fixed is not { } own
            || field.Header.TranslationKey is not null
            || field.Header.Prefixes.Any(prefix => prefix.TranslationKey is not null))
        {
            return null;
        }

        return string.Concat(
                field
                    .Header
                    .Prefixes
                    .Select(prefix => prefix.Fixed))
            + own;
    }

    /// <summary>
    /// Adds one configuration error per group of headers that the header-name import cannot tell apart: equal after
    /// trimming and ignoring case. Blank headers name no column and are left out.
    /// </summary>
    public static void AddIndistinct(
        IEnumerable<(FlatField Field, string? Header)> headers,
        List<TabularError> errors
    )
    {
        // WHY: Each indistinct group adds one configuration error; a query would hide that side effect.
        // ReSharper disable once LoopCanBeConvertedToQuery
        foreach (var group in headers
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Header))
            .GroupBy(entry => entry.Header!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1))
        {
            var first = group.First();
            errors.Add(
                TabularError.Configuration(
                    $"The headers of {string.Join(", ", group.Select(entry => entry.Field.Path.Value))} are equal after trimming and ignoring case; the header-name import could not tell them apart.",
                    new TabularErrorLocation
                    {
                        ColumnKey = first.Field.Key,
                        PropertyPath = first.Field.Path,
                    }));
        }
    }

    private static bool TryText(
        string? key,
        string? fixedText,
        FlatField field,
        ConversionContext context,
        out string text,
        out TabularError? error
    )
    {
        error = null;
        if (Translations.Resolve(
            key,
            fixedText
            ?? (key is null
                ? string.Empty
                : null),
            context,
            out text,
            out var failure))
        {
            return true;
        }

        error = TabularError.Create(
            failure.Code!,
            TabularErrorKind.Configuration,
            failure.Message!,
            new TabularErrorLocation
            {
                ColumnKey = field.Key,
                PropertyPath = field.Path,
            });

        return false;
    }
}
