# Excel

## Formats

Imports read XLSX Transitional, XLSX Strict, and XLSM; macros are never run.
XLS, XLSB, and encrypted workbooks are `tabular.schema.unsupportedFormat`.
`ExpectedVariant` restricts an import to `ExcelVariant.Xlsx` or
`ExcelVariant.Xlsm`. Exports write XLSX Transitional.

## Reading

- `SheetName` chooses a sheet; without it, a workbook with one sheet is read
  and several sheets are `tabular.schema.ambiguousSheet`.
- `StartRow` and `EndRow` select the table; `RowNumber` is always the sheet row.
- `VisibleRowsOnly` skips rows hidden by Excel.
- Cell values outside the selection are not read, so they cause no error; the
  XML of the whole sheet is still checked.
- Rows with only formatting are skipped. Stored formula results are read like
  values; a missing result or an error value is a field error, and
  `RejectFormulas()` rejects every formula in a column.

## Writing

Numbers, dates, and times are native cells with the number format of the map.
Text stays text, also when it looks like a number or formula.

```csharp
var options = new ExcelExportOptions
{
    SheetName = TabularSheetName.Create("Contacts"),
    WriteHeader = true,
    Layout = new ExcelLayoutOptions
    {
        Mode = ExcelLayoutMode.Table,
        HeaderFill = ExcelColor.Create("#3A5A8C"),
        HeaderFontColor = ExcelColor.Create("#FFFFFF"),
        HeaderBold = true,
        Columns = new Dictionary<ColumnKey, ExcelColumnLayout> { [ColumnKey.Create("email")] = new() { Width = 30 } }
    }
};
```

Table mode needs a header, contiguous columns, and unique headers. Filter and
frozen header default to `Auto`: present when there is a header and at least
one data row. Excel limits (1,048,576 rows, 16,384 columns, 32,767 characters
per cell) are `tabular.schema.limitExceeded`. Target paths ending in `.xls`,
`.xlsm`, or `.xlsb` are rejected.

## Several sheets

```csharp
var workbook = ExcelExport.Create()
    .Add(TabularSheetName.Create("Customers"), customers, customerMap, new ExcelSheetExportOptions { WriteHeader = true })
    .Add(TabularSheetName.Create("Orders"), orders, orderMap)
    .Build();
var result = await excel.ExportAsync(workbook, path, cancellationToken);

var definition = ExcelImport.Create();
var customerSheet = definition.Add(TabularSheetName.Create("Customers"), customerMap);
var orderSheet = definition.Add(TabularSheetName.Create("Orders"), orderMap, orderTarget);
var report = await excel.ImportAsync(stream, definition.Build(), cancellationToken);
var customers = report.Get(customerSheet).Items;
```

A strict import covers all sheets; `TabularWriteOptions.TransactionPerSheet`
gives each sheet its own transaction.
