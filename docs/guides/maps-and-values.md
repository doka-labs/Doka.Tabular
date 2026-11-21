# Maps and values

## Columns and keys

`Column(property, index, key, configure)` maps a property to a zero-based column
index. The key defaults to the property name; keys are unique in a map, compared
with `OrdinalIgnoreCase`, and select export columns with `ColumnKeys`.

System.Text.Json reads and writes a `ColumnKey` as a string, so a request can
carry the selection directly:

```csharp
public sealed record ContactExportRequest(IReadOnlyList<ColumnKey>? Columns);

// {"Columns":["email","lastName"]}
var options = new TabularExportOptions { ColumnKeys = request.Columns };
```

A text that is not a valid key fails deserialization with a `JsonException`
naming its JSON path. A key that the map does not know is a configuration error
in the export report.

| Option | Effect |
| --- | --- |
| `Header(text)`, `HeaderTranslationKey(key)` | Header text on export and for `ByHeaderName` |
| `Required()` | An empty value is a field error |
| `Validate(rule, code:, alsoOnExport:)` | Own rule after conversion |
| `ImportOnly()`, `ExportOnly()` | Column used in one direction only |
| `OptionalHeader()` | Missing header gives the empty value (nullable fields) |
| `IncludeSourceValue()` | Errors of this column carry the source text |
| `Trim()`, `Culture(culture)` | Per-field text handling |
| `ExcelNumberFormat(code)`, `ExcelAsText()` | Excel cell display and type |
| `NullText(text)`, `EmptyTranslationKey(key)` | Distinguish null from the empty string |

## Creating records

Exactly one fitting public constructor is used; parameters bind to mapped
properties by C# name. Remaining mapped properties are set through `init` or
`set`. Several fitting constructors need a factory. The factory runs once in
`Build()`: it prepares the fields it reads and returns the function that creates
each record, so a record costs no property lookup:

```csharp
var map = TabularMap.For<Contact>()
    .Column(x => x.Id, index: 0)
    .Column(x => x.Email, index: 1)
    .UseFactory(fields =>
    {
        var id = fields.Of(x => x.Id);
        var email = fields.Of(x => x.Email);
        return values => new Contact(values.Get(id), values.Get(email));
    })
    .Expect<InvalidContactException>(TabularErrorCode.Create("contact.invalid"))
    .Build();
```

A field of a property that is not mapped is a configuration error of `Build()`;
a field of a property the call does not import is a configuration error before
the first row.

`Expect<TException>(code)` turns exceptions of constructors, factories, and
converters into row errors; other exceptions end the run as technical errors.

## Nested maps and row maps

```csharp
var order = TabularMap.For<Order>()
    .Column(x => x.Number, index: 0)
    .Nested(x => x.Billing, addressMap, e => e.StartIndex(1).KeyPrefix(ColumnKey.Create("billing")).HeaderPrefix("Billing "))
    .Nested(x => x.Shipping, addressMap, e => e.StartIndex(3).KeyPrefix(ColumnKey.Create("shipping")).HeaderPrefix("Shipping "))
    .Build();

var rows = RowMap.Create();
var user = rows.Add(userMap);
var address = rows.Add(addressMap, e => e.StartIndex(12));
var rowMap = rows.Build();
// Each imported TabularRow returns its records with row.Get(user) and row.Get(address).
```

An optional nested record is null when all its fields are empty. A row is taken
over only when all its records are valid.

## Composed values

```csharp
.Compose(x => x.Amount,
    c => c.Field<long>(index: 3, key: ColumnKey.Create("amountMinor")),
    c => c.Field<string>(index: 4, key: ColumnKey.Create("currency")),
    read: (minor, currency) => new Money(minor / 100m, currency),
    write: money => ((long)(money.Amount * 100), money.Currency))
```

`read` runs only when every part is valid.

## Conversion

Conversions are exact. A value that cannot be represented, such as a decimal
with too many digits, is a field error unless `Rounding` is configured. Own
types convert through a built-in base type:

```csharp
.Column(x => x.Number, index: 0, configure: c => c.Convert<string, CustomerNumber>(
    read: text => CustomerNumber.Parse(text),
    write: number => number.ToString()))
```

Text fields keep their content; `PadLeft(5, '0')` restores leading zeros of
postal codes read from number cells. An empty text field is the empty string;
`EmptyStringAsNull()` makes it null for `string?` fields.

## Enums and flags

Names and `JsonStringEnumMemberName` texts are read without regard to case.
Numbers need `AllowNumericInput()`. Flags are written as the exactly matching
member, otherwise as single bits in ascending order, with `,` or the separator
from `FlagsSeparator`. Aliases need `ExportAlias`,
`ExportAsNumber`, or `TranslationKeys`. Aliases share one value, so
`ExportAlias` takes the member name: `c.ExportAlias(nameof(Status.Enabled))`;
`Build()` rejects a name that is no member.

## Date and time

ISO texts are always read; culture texts use the culture's short date pattern
and must match it exactly. Local times without offset need `FileTimeZone` for
`DateTimeOffset`; gaps and overlaps need `ShiftInvalidTime` and
`ResolveAmbiguousTime`. Excel serial numbers are read in the workbook's date
system; numbers without a date format need `AllowExcelSerialNumbers()`. More
than seven fraction digits need `Rounding` unless the further digits are zeros.

## Translation

Maps carry keys and fixed texts; each call passes a dictionary:

```csharp
var options = new TabularExportOptions
{
    WriteHeader = true,
    Translations = new Dictionary<string, string> { ["email"] = "E-Mail" },
    MissingTranslation = MissingTranslationBehavior.UseFixedText
};
```

With the default `MissingTranslationBehavior.Error`, a needed key that is
missing is `tabular.translation.missing` before the first write. A call takes
the dictionary at its start: a change during a running export does not affect
it, the next call uses the changed texts.
