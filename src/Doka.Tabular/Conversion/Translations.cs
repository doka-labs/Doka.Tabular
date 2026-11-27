namespace Doka.Tabular;

internal static class Translations
{
    /// <summary>Resolves a text by the export order: translation, otherwise fixed text, following <c>MissingTranslation</c>.</summary>
    public static bool Resolve(
        string? key,
        string? fixedText,
        ConversionContext context,
        out string text,
        out ConversionFailure failure
    )
    {
        failure = default;
        if (key is null
            || context.Translations is null)
        {
            text = fixedText ?? string.Empty;
            if (fixedText is not null)
            {
                return true;
            }

            failure = new ConversionFailure(
                TabularErrorCodes.Translation.Missing,
                "No text is available for the value.");

            return false;
        }

        if (context.Translations.TryGetValue(key, out var translated))
        {
            if (translated.Length > 0)
            {
                text = translated;

                return true;
            }

            text = string.Empty;
            failure = new ConversionFailure(
                TabularErrorCodes.Configuration.Invalid,
                $"The translation of key {key} is empty.");

            return false;
        }

        if (context.MissingTranslation == MissingTranslationBehavior.UseFixedText
            && fixedText is not null)
        {
            text = fixedText;

            return true;
        }

        text = string.Empty;
        failure = new ConversionFailure(
            TabularErrorCodes.Translation.Missing,
            $"The translation key {key} is missing.");

        return false;
    }
}
