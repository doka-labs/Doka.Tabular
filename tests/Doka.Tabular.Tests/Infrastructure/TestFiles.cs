// WHY: The core and the Excel test applications link this file; each uses only some of its members.
// ReSharper disable UnusedMember.Global
// ReSharper disable MemberCanBePrivate.Global

using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;

namespace Doka.Tabular.Tests.Infrastructure;

/// <summary>
/// Creates the same rows as CSV text or as an XLSX workbook written independently with the Open XML SDK, and reads
/// exported files back as cell texts. Strings become text cells, numbers numeric cells, booleans boolean cells.
/// </summary>
internal static class TestFiles
{
    public static readonly CsvFormat CsvInstance = new(new CsvFormatOptions());

    public static readonly CheckedExcelFormat ExcelInstance = new(new ExcelFormat(new ExcelFormatOptions()));

    public static readonly TabularImport Import = new(CsvInstance, ExcelInstance.Format);

    public static readonly CheckedTabularExport Export = new(new TabularExport(CsvInstance, ExcelInstance.Format));

    public static TheoryData<TabularType> Types => [TabularType.Csv, TabularType.Excel];

    public static CancellationToken Token => TestContext.Current.CancellationToken;

    public static MemoryStream Create(
        TabularType type,
        params object?[][] rows
    ) => type == TabularType.Csv
        ? CsvText(rows)
        : Workbook(("Sheet1", rows));

    public static MemoryStream CsvText(
        params object?[][] rows
    ) => TestInput.Utf8(
        string.Join(
            "\r\n",
            rows.Select(row => row is [var single]
                && Text(single)
                    .Length
                == 0
                    ? "\"\""
                    : string.Join(",", row.Select(cell => Quote(Text(cell)))))));

    public static MemoryStream Workbook(
        params (string Name, object?[][] Rows)[] sheets
    ) => Workbook(false, null, sheets);

    /// <summary>Creates a workbook; <paramref name="dateFormatColumns" /> get the built-in date format 14 on every row.</summary>
    public static MemoryStream Workbook(
        bool date1904,
        IReadOnlySet<int>? dateFormatColumns,
        params (string Name, object?[][] Rows)[] sheets
    )
    {
        var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbook = document.AddWorkbookPart();
            workbook.Workbook = new Workbook(new WorkbookProperties { Date1904 = date1904 }, new Sheets());
            var styles = workbook.AddNewPart<WorkbookStylesPart>();
            styles.Stylesheet = new Stylesheet(
                new Fonts(new Font()) { Count = 1 },
                new Fills(
                    new Fill(new PatternFill { PatternType = PatternValues.None }),
                    new Fill(new PatternFill { PatternType = PatternValues.Gray125 })) { Count = 2 },
                new Borders(new Border()) { Count = 1 },
                new CellStyleFormats(new CellFormat()) { Count = 1 },
                new CellFormats(
                    new CellFormat(),
                    new CellFormat
                    {
                        NumberFormatId = 14,
                        ApplyNumberFormat = true,
                    }) { Count = 2 });

            uint id = 1;
            foreach (var (name, rows) in sheets)
            {
                var part = workbook.AddNewPart<WorksheetPart>();
                var data = new SheetData();
                for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
                {
                    var row = new Row { RowIndex = (uint)(rowIndex + 1) };
                    for (var column = 0; column < rows[rowIndex].Length; column++)
                    {
                        if (CellFor(rows[rowIndex][column], ColumnName(column) + (rowIndex + 1)) is { } cell)
                        {
                            if (dateFormatColumns?.Contains(column) is true)
                            {
                                cell.StyleIndex = 1;
                            }

                            row.Append(cell);
                        }
                    }

                    data.Append(row);
                }

                part.Worksheet = new Worksheet(data);
                workbook.Workbook.Sheets!.Append(
                    new Sheet
                    {
                        Id = workbook.GetIdOfPart(part),
                        SheetId = id++,
                        Name = name,
                    });
            }
        }

        stream.Position = 0;

        return stream;
    }

    /// <summary>
    /// Opens a Transitional workbook after an independent schema check with the Open XML SDK; every workbook a test
    /// reads through this class is checked. Strict input is covered by the reference files.
    /// </summary>
    public static SpreadsheetDocument OpenWorkbook(
        Stream file
    )
    {
        Assert.Empty(WorkbookProblems(file));
        file.Position = 0;

        return SpreadsheetDocument.Open(file, isEditable: false);
    }

    /// <summary>The schema problems of a workbook, each with its part.</summary>
    public static IReadOnlyList<string> WorkbookProblems(
        Stream workbook
    )
    {
        workbook.Position = 0;
        using var document = SpreadsheetDocument.Open(workbook, isEditable: false);

        return
        [
            .. new OpenXmlValidator()
                .Validate(document)
                .Select(error => $"{error.Part?.Uri}: {error.Description}"),
        ];
    }

    /// <summary>Reads a CSV or XLSX file as rows of cell texts; numbers keep their stored invariant text.</summary>
    public static List<string?[]> Cells(
        TabularType type,
        Stream file,
        string? sheet = null,
        char delimiter = ','
    )
    {
        file.Position = 0;
        if (type == TabularType.Csv)
        {
            return ParseCsv(TestInput.Utf8Text(file), delimiter);
        }

        using var document = OpenWorkbook(file);
        var workbook = document.WorkbookPart!;
        var target = workbook.Workbook!.Sheets!
            .Elements<Sheet>()
            .First(entry => sheet is null || entry.Name == sheet);

        var part = (WorksheetPart)workbook.GetPartById(target.Id!);
        var shared = workbook.SharedStringTablePart?.SharedStringTable!
            .Elements<SharedStringItem>()
            .Select(item => item.InnerText)
            .ToList();

        var result = new List<string?[]>();
        foreach (var row in part.Worksheet!.GetFirstChild<SheetData>()!.Elements<Row>())
        {
            var cells = row
                .Elements<Cell>()
                .ToList();

            var width = cells.Count == 0
                ? 0
                : cells.Max(cell => ColumnIndex(cell.CellReference!.Value!)) + 1;

            var values = new string?[width];
            foreach (var cell in cells)
            {
                values[ColumnIndex(cell.CellReference!.Value!)] = CellText(cell, shared);
            }

            result.Add(values);
        }

        return result;
    }

    /// <summary>Returns the cell type of every cell of a row of an exported workbook.</summary>
    public static List<string> CellTypes(
        Stream file,
        int rowNumber
    )
    {
        using var document = OpenWorkbook(file);
        var part = document.WorkbookPart!.WorksheetParts.First();
        var row = part.Worksheet!.GetFirstChild<SheetData>()!
            .Elements<Row>()
            .Single(entry => entry.RowIndex!.Value == rowNumber);

        return
        [
            .. row
                .Elements<Cell>()
                .Select(cell => cell.DataType?.Value switch
                {
                    null => "n",
                    var value when value == CellValues.Boolean => "b",
                    var value when value == CellValues.InlineString => "inlineStr",
                    var value when value == CellValues.SharedString => "s",
                    var value when value == CellValues.String => "str",
                    var value when value == CellValues.Number => "n",
                    _ => "other",
                }),
        ];
    }

    private static string? CellText(
        Cell cell,
        List<string>? shared
    )
    {
        if (cell.DataType?.Value == CellValues.InlineString)
        {
            return cell.InlineString!.InnerText;
        }

        return cell.DataType?.Value == CellValues.SharedString
            ? shared![int.Parse(cell.CellValue!.Text, CultureInfo.InvariantCulture)]
            : cell.CellValue?.Text;
    }

    private static Cell? CellFor(
        object? value,
        string reference
    ) => value switch
    {
        null => null,
        string text => new Cell
        {
            CellReference = reference,
            DataType = CellValues.InlineString,
            InlineString = new InlineString(new Text(text) { Space = SpaceProcessingModeValues.Preserve }),
        },
        bool flag => new Cell
        {
            CellReference = reference,
            DataType = CellValues.Boolean,
            CellValue = new CellValue(
                flag
                    ? "1"
                    : "0"),
        },
        IFormattable number => new Cell
        {
            CellReference = reference,
            CellValue = new CellValue(number.ToString(null, CultureInfo.InvariantCulture)),
        },
        _ => throw new ArgumentException($"Unsupported cell value {value.GetType().Name}.", nameof(value)),
    };

    private static string Text(
        object? value
    ) => value switch
    {
        null => string.Empty,
        bool flag => flag
            ? "true"
            : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()!,
    };

    private static string Quote(
        string text
    ) => text
            .AsSpan()
            .IndexOfAny(",\"\r\n")
        >= 0
            ? "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : text;

    private static string ColumnName(
        int index
    )
    {
        var name = string.Empty;
        for (var value = index + 1; value > 0; value = (value - 1) / 26)
        {
            name = (char)('A' + (value - 1) % 26) + name;
        }

        return name;
    }

    private static int ColumnIndex(
        string reference
    )
    {
        var index = 0;

        // WHY: The column number is accumulated letter by letter in base 26.
        // ReSharper disable once LoopCanBeConvertedToQuery
        foreach (var character in reference.TakeWhile(char.IsAsciiLetter))
        {
            index = index * 26 + (character - 'A') + 1;
        }

        return index - 1;
    }

    private static List<string?[]> ParseCsv(
        string text,
        char delimiter
    )
    {
        var rows = new List<string?[]>();
        var fields = new List<string?>();
        var field = new StringBuilder();
        var quoted = false;
        var started = false;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            started = true;
            if (quoted)
            {
                // WHY: The branches inspect the next character and move the index; a switch would hide that.
                // ReSharper disable once ConvertIfStatementToSwitchStatement
                if (character == '"'
                    && index + 1 < text.Length
                    && text[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else if (character == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(character);
                }
            }
            else if (character == '"')
            {
                quoted = true;
            }
            else if (character == delimiter)
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else if (character == '\r'
                && index + 1 < text.Length
                && text[index + 1] == '\n')
            {
                fields.Add(field.ToString());
                field.Clear();
                rows.Add([.. fields]);
                fields.Clear();
                index++;
                started = false;
            }
            else
            {
                field.Append(character);
            }
        }

        if (started)
        {
            fields.Add(field.ToString());
            rows.Add([.. fields]);
        }

        return rows;
    }
}
