using System.IO.Compression;
using System.Xml;

namespace Doka.Tabular.Excel;

internal enum ExcelPackageVariant
{
    Transitional,
    Macro,
}

internal sealed record ExcelSheetInfo(
    TabularSheetName Name,
    string PartPath
);

/// <summary>An opened workbook package: sheets, shared strings, styles, and the date system.</summary>
internal sealed partial class ExcelPackage : IAsyncDisposable
{
    private const string TransitionalRelationships =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private const string StrictRelationships = "http://purl.oclc.org/ooxml/officeDocument/relationships";

    // WHY: Part names cannot contain '*', so the prefix keeps extension defaults apart from part overrides.
    private const string DefaultKey = "*.";
    private readonly ZipArchive _archive;
    private readonly ExpansionBudget _budget;

    private ExcelPackage(
        ZipArchive archive,
        ExpansionBudget budget,
        ExcelPackageVariant variant
    )
    {
        _archive = archive;
        _budget = budget;
        Variant = variant;
    }

    private ExcelPackageVariant Variant { get; }

    public IReadOnlyList<ExcelSheetInfo> Sheets { get; private set; } = [];

    public bool Uses1904DateSystem { get; private set; }

    /// <summary>
    /// Gets the shared strings; <see langword="null" /> stands for a text beyond the field limit, which is not kept
    /// and is an error only when a cell inside the read range uses it.
    /// </summary>
    public IReadOnlyList<string?> SharedStrings { get; private set; } = [];

    public IReadOnlyList<NumberFormatKind> CellFormats { get; private set; } = [];

    private long? MaxFieldCharacters { get; init; }

    public ExcelVariant DetectedVariant => Variant == ExcelPackageVariant.Macro
        ? ExcelVariant.Xlsm
        : ExcelVariant.Xlsx;

    public ValueTask DisposeAsync() => _archive.DisposeAsync();

    public static async Task<(ExcelPackage? Package, TabularError? Error)> OpenAsync(
        Stream input,
        ResourceLimits limits,
        CancellationToken cancellationToken
    )
    {
        var head = new byte[8];
        var length = await ReadPrefixAsync(input, head, cancellationToken)
            .ConfigureAwait(false);

        input.Position = 0;
        if (SignatureError(head.AsSpan(0, length)) is { } signatureError)
        {
            return (null, signatureError);
        }

        if (await OpenArchiveAsync(input, cancellationToken)
                .ConfigureAwait(false) is not { } archive)
        {
            return (null, Corrupt("The ZIP container is damaged."));
        }

        try
        {
            var (package, error) = await LoadAsync(archive, limits, cancellationToken)
                .ConfigureAwait(false);

            if (package is null)
            {
                await archive
                    .DisposeAsync()
                    .ConfigureAwait(false);
            }

            return (package, error);
        }
        catch (XmlException)
        {
            await archive
                .DisposeAsync()
                .ConfigureAwait(false);

            return (null, Corrupt("A part of the workbook is not well-formed XML."));
        }
        catch (InvalidDataException)
        {
            await archive
                .DisposeAsync()
                .ConfigureAwait(false);

            return (null, Corrupt("The ZIP container is damaged."));
        }
        catch
        {
            await archive
                .DisposeAsync()
                .ConfigureAwait(false);

            throw;
        }
    }

    private static async Task<ZipArchive?> OpenArchiveAsync(
        Stream input,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await ZipArchive
                .CreateAsync(input, ZipArchiveMode.Read, leaveOpen: true, entryNameEncoding: null, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>Finds the workbook part and its type and loads it; a package without workbook is unsupported.</summary>
    private static async Task<(ExcelPackage? Package, TabularError? Error)> LoadAsync(
        ZipArchive archive,
        ResourceLimits limits,
        CancellationToken cancellationToken
    )
    {
        var budget = new ExpansionBudget(limits.MaxExpandedBytes);
        var contentTypes = await ReadContentTypesAsync(archive, budget, cancellationToken)
            .ConfigureAwait(false);

        var workbookPath = await FindWorkbookAsync(archive, budget, cancellationToken)
            .ConfigureAwait(false);

        if (workbookPath is null
            || ContentTypeOf(contentTypes, workbookPath) is not { } workbookType)
        {
            return (null, Unsupported("The ZIP container does not contain a workbook."));
        }

        ExcelPackageVariant? variant = workbookType switch
        {
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml" => ExcelPackageVariant
                .Transitional,
            "application/vnd.ms-excel.sheet.macroEnabled.main+xml" => ExcelPackageVariant.Macro,
            _ => null,
        };

        if (variant is not { } known)
        {
            return (null, Unsupported(
                workbookType.Contains("binary", StringComparison.OrdinalIgnoreCase)
                    ? "XLSB workbooks are not supported."
                    : "The workbook type is not supported."));
        }

        var package = new ExcelPackage(archive, budget, known) { MaxFieldCharacters = limits.MaxFieldCharacters };
        await package
            .LoadWorkbookAsync(workbookPath, cancellationToken)
            .ConfigureAwait(false);

        return (package, null);
    }

    private static TabularError? SignatureError(
        ReadOnlySpan<byte> head
    )
    {
        if (ContentSignatures.IsCfb(head))
        {
            return Unsupported("The content is a CFB container (XLS or an encrypted workbook); it is not supported.");
        }

        return ContentSignatures.IsZip(head)
            ? null
            : TabularError.Create(
                TabularErrorCodes.Schema.FormatMismatch,
                TabularErrorKind.Data,
                "Excel was specified, but the content is not a container.");
    }

    public async Task<Stream> OpenPartAsync(
        string path,
        CancellationToken cancellationToken
    )
    {
        var entry = _archive.GetEntry(path) ?? throw new InvalidDataException($"The part {path} is missing.");

        return new ExpansionCountingStream(
            await entry
                .OpenAsync(cancellationToken)
                .ConfigureAwait(false),
            _budget);
    }

    /// <summary>
    /// Checks stored text while it is read. An escape spells one character with seven, so the stored text may be up to
    /// seven times the limit; the decoded value is checked with <see cref="CheckLength" />.
    /// </summary>
    public void CheckStoredLength(
        long length
    )
    {
        if (MaxFieldCharacters is { } limit
            && length / 7 > limit)
        {
            throw TabularResourceException.Limit("field characters", limit);
        }
    }

    /// <summary>Gets whether a decoded field value is beyond the field limit of UTF-16 code units.</summary>
    private bool ExceedsFieldLimit(
        long length
    ) => MaxFieldCharacters is { } limit && length > limit;

    /// <summary>Gets whether a stored text is certainly beyond the field limit, also after decoding its escapes.</summary>
    private bool IsOverlong(
        long storedLength
    ) => MaxFieldCharacters is { } limit && storedLength / 7 > limit;

    /// <summary>The error of a field beyond the limit; only an overlong text, which needs a limit, raises it.</summary>
    public TabularResourceException FieldLimitExceeded() => TabularResourceException.Limit(
        "field characters",
        MaxFieldCharacters.GetValueOrDefault());

    /// <summary>Checks a decoded field value against the limit of UTF-16 code units.</summary>
    public void CheckLength(
        long length
    )
    {
        if (MaxFieldCharacters is { } limit
            && length > limit)
        {
            throw TabularResourceException.Limit("field characters", limit);
        }
    }

    private static async Task<int> ReadPrefixAsync(
        Stream input,
        byte[] buffer,
        CancellationToken cancellationToken
    )
    {
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await input
                .ReadAsync(buffer.AsMemory(length), cancellationToken)
                .ConfigureAwait(false);

            if (read == 0)
            {
                break;
            }

            length += read;
        }

        return length;
    }

    private static TabularError Unsupported(
        string message
    ) => TabularError.Create(TabularErrorCodes.Schema.UnsupportedFormat, TabularErrorKind.Data, message);

    private static TabularError Corrupt(
        string message
    ) => TabularError.Create(TabularErrorCodes.Parsing.InvalidSyntax, TabularErrorKind.Data, message);

    private sealed record Relationship(
        string Type,
        string Target
    );
}
