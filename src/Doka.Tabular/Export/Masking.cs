namespace Doka.Tabular;

internal static class Masking
{
    /// <summary>Replaces the hidden characters of <paramref name="text" /> in place; the length stays the same.</summary>
    public static void Apply(
        Span<char> text,
        FieldMask mask
    )
    {
        if (text.Length <= mask.VisibleCount)
        {
            text.Fill(mask.MaskCharacter);

            return;
        }

        var hidden = text.Length - mask.VisibleCount;
        if (mask.Mode == MaskMode.ExceptLast)
        {
            text[..hidden]
                .Fill(mask.MaskCharacter);
        }
        else
        {
            text[mask.VisibleCount..]
                .Fill(mask.MaskCharacter);
        }
    }
}
