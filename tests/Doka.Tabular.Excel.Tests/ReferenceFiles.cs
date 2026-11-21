namespace Doka.Tabular.Excel.Tests;

/// <summary>The Excel installations that saved a set of reference files.</summary>
public enum ReferenceSet
{
    /// <summary>Excel for Mac with English system language; German input such as 01.04.1980 stays text.</summary>
    MacEnglish,

    /// <summary>Excel for Windows with German system language.</summary>
    WindowsGerman,
}

/// <summary>Locates the Excel reference files; a missing file skips its test visibly.</summary>
internal static class ReferenceFiles
{
    public static string Path(
        ReferenceSet set,
        string number,
        string fileName
    )
    {
        var folder = set switch
        {
            ReferenceSet.MacEnglish => "mac-en",
            ReferenceSet.WindowsGerman => "win-de",
            _ => throw new ArgumentOutOfRangeException(nameof(set), set, null),
        };

        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", folder, fileName);
        Assert.SkipUnless(File.Exists(path), $"reference file {number} not yet provided");

        return path;
    }

    public static async Task<MemoryStream> CopyAsync(
        string path
    )
    {
        var copy = new MemoryStream(await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        copy.Position = 0;

        return copy;
    }
}
