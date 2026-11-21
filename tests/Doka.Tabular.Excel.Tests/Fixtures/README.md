# Excel reference files

These workbooks are the independent source for how Excel writes the format. They are created with Excel, never by
Tabular or by test code, and contain no real data. Every reference test runs once per set. A test whose file is
missing skips with the reason `reference file Rnn not yet provided`.

## Sets

| Folder | Excel version | Operating system | System language |
| --- | --- | --- | --- |
| `mac-en/` | Excel for Mac 16.115 (26110837) | macOS 26.5.2 (25F84) | English |
| `win-de/` | Excel 2610 (Build 20527.20194) | Windows 11 26H2 (Build 26300.9974) | German |

Both sets were entered with the same German values. The sets differ in what Excel stored:

- `mac-en/`: German date input such as `01.04.1980` stays a text cell, even in a cell with a date format. The tests
  read these files with the culture `de-DE` and also check that the invariant culture rejects the text dates.
- `mac-en/`: times use custom number formats such as `hh:mm:ss`; `win-de/` uses the built-in formats 14, 21 and 22.
- `win-de/`: `R11-legacy.xls` and `R12-binary.xlsb` were saved from an earlier state of R01. Their content does not
  matter; both files are only checked for rejection.

## Files

| File | Saved as | Content | Purpose |
| --- | --- | --- | --- |
| `R01-transitional.xlsx` | Excel Workbook | Sheet `Benutzer` with numbers, texts, shared strings, booleans, dates, times, durations, leading zeros, formulas with results and errors, escaped text, Unicode | Cell types and conversion of an ordinary workbook |
| `R02-strict.xlsx` | Strict Open XML Workbook | R01 saved as Strict; dates, times and durations are ISO 8601 date cells | Same values and diagnostics as R01 |
| `R03-macro.xlsm` | Excel Macro-Enabled Workbook | R01 with a `Workbook_Open` macro that writes `Z1` | Macros are never run; expected variant check |
| `R04-two-sheets.xlsx` | Excel Workbook | Sheets `Hinweise` and `Benutzer` | Sheet selection |
| `R05-read-range.xlsx` | Excel Workbook | Sheet `Bericht` with title rows, header in row 4, empty and formatted rows, a formula with empty result, a sum in row 82 | Start row, end row, empty rows |
| `R06-visibility.xlsx` | Excel Workbook | Sheet `Daten` with an autofilter on columns A and B; the amounts are formulas | Visible-rows selection |
| `R07-date-system-1904.xlsx` | Excel Workbook | Sheet `Daten` in the 1904 date system | 1904 serial numbers |
| `R08-protected.xlsx` | Excel Workbook | R04 `Benutzer` with sheet and structure protection, no open password | Protection does not hinder reading |
| `R09-table-object.xlsx` | Excel Workbook | Table object `Benutzer`, style `TableStyleMedium2` | Reading tables; comparison with the table export |
| `R10-encrypted.xlsx` | Excel Workbook with open password | Encrypted OOXML package | `tabular.schema.unsupportedFormat` |
| `R11-legacy.xls` | Excel 97-2003 or 97-2004 Workbook | R01 as BIFF8 | `tabular.schema.unsupportedFormat` |
| `R12-binary.xlsb` | Excel Binary Workbook | R01 as XLSB | `tabular.schema.unsupportedFormat` |

The text in `B6` of R01 has one space before and after it.
