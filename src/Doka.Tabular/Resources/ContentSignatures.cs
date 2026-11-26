namespace Doka.Tabular;

/// <summary>The leading bytes of the containers a format check recognizes.</summary>
internal static class ContentSignatures
{
    /// <summary>
    /// Gets whether the content starts like a ZIP archive: with a local file header, or with the end of the central
    /// directory as an archive without entries does.
    /// </summary>
    public static bool IsZip(
        ReadOnlySpan<byte> head
    ) => head.StartsWith((ReadOnlySpan<byte>)[0x50, 0x4B, 0x03, 0x04])
        || head.StartsWith((ReadOnlySpan<byte>)[0x50, 0x4B, 0x05, 0x06]);

    /// <summary>Gets whether the content is a Compound File Binary container, such as XLS.</summary>
    public static bool IsCfb(
        ReadOnlySpan<byte> head
    ) => head.StartsWith((ReadOnlySpan<byte>)[0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]);
}
