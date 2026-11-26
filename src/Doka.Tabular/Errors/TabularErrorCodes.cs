namespace Doka.Tabular;

/// <summary>
/// The error codes of Doka.Tabular, grouped by the segment after <c>tabular.</c>; for example
/// <see cref="Schema.MissingColumn" /> is <c>tabular.schema.missingColumn</c>.
/// </summary>
/// <remarks>
/// A code keeps its meaning across formats and versions; <see cref="TabularError.Message" /> and
/// <see cref="TabularError.Location" /> tell the single case apart. An application reacts to the code, never to the
/// message text. A code the application assigns itself, for example to a field rule, replaces the library code.
/// </remarks>
[PublicAPI]
public static class TabularErrorCodes
{
    /// <summary>Gets all library codes in register order.</summary>
    public static IReadOnlyList<TabularErrorCode> All { get; } =
    [
        Configuration.Invalid,
        Schema.MissingColumn,
        Schema.AmbiguousColumn,
        Schema.MissingSheet,
        Schema.AmbiguousSheet,
        Schema.FormatMismatch,
        Schema.UnsupportedFormat,
        Schema.LimitExceeded,
        Parsing.InvalidSyntax,
        Parsing.InvalidEncoding,
        Conversion.MissingValue,
        Conversion.InvalidFormat,
        Conversion.OutOfRange,
        Conversion.PrecisionLoss,
        Conversion.UnsupportedValue,
        Conversion.MissingRepresentation,
        Translation.Missing,
        Validation.Failed,
        Key.Incomplete,
        Key.Duplicate,
        Lookup.NotFound,
        Lookup.NotUnique,
        Callback.Failed,
        Callback.InvalidResult,
        Persistence.Conflict,
        Persistence.WriteFailed,
        Persistence.OperationFailed,
        Persistence.CommitUnknown,
        Persistence.RollbackUnknown,
        Persistence.ContextRestoreFailed,
        Io.Failed,
        Resource.LimitExceeded,
    ];

    /// <summary>Codes starting with <c>tabular.configuration.</c>.</summary>
    public static class Configuration
    {
        /// <summary>Gets <c>tabular.configuration.invalid</c>: the map or the call cannot work as configured.</summary>
        /// <remarks>
        /// Kind <see cref="TabularErrorKind.Configuration" />. A map with structural errors throws
        /// <see cref="TabularConfigurationException" /> with this code at <c>Build()</c>. A call reports it before
        /// the first read or write when its options, the EF Core model, the transaction, the journal or the storage
        /// adapter do not fit, or when enum texts or translations collide. Correct the map or the options; repeating
        /// the call unchanged gives the same result.
        /// </remarks>
        public static TabularErrorCode Invalid { get; } = TabularErrorCode.Create("tabular.configuration.invalid");
    }

    /// <summary>Codes starting with <c>tabular.schema.</c>.</summary>
    public static class Schema
    {
        /// <summary>Gets <c>tabular.schema.missingColumn</c>: a mapped column is not in the file.</summary>
        /// <remarks>
        /// Kind <see cref="TabularErrorKind.Data" />. A required header is missing, or a CSV record is shorter than a
        /// mapped position. Ask for a file with the column, or mark a header that may be absent with
        /// <c>OptionalHeader()</c>.
        /// </remarks>
        public static TabularErrorCode MissingColumn { get; } = TabularErrorCode.Create("tabular.schema.missingColumn");

        /// <summary>Gets <c>tabular.schema.ambiguousColumn</c>: several headers match one mapped column.</summary>
        /// <remarks>
        /// Kind <see cref="TabularErrorKind.Data" />. After header normalization, more than one header of the file
        /// matches the same field. Ask for a file with unique headers, or narrow the header normalization.
        /// </remarks>
        public static TabularErrorCode AmbiguousColumn { get; } =
            TabularErrorCode.Create("tabular.schema.ambiguousColumn");

        /// <summary>Gets <c>tabular.schema.missingSheet</c>: the selected worksheet is not in the workbook.</summary>
        /// <remarks>
        /// Kind <see cref="TabularErrorKind.Data" />. The sheet named in the options or assigned to a map does not
        /// exist. Ask for a workbook with that sheet, or select another one.
        /// </remarks>
        public static TabularErrorCode MissingSheet { get; } = TabularErrorCode.Create("tabular.schema.missingSheet");

        /// <summary>
        /// Gets <c>tabular.schema.ambiguousSheet</c>: the workbook has several sheets and none is selected.
        /// </summary>
        /// <remarks>
        /// Kind <see cref="TabularErrorKind.Data" />. A single-sheet import found more than one worksheet. Select
        /// the sheet by name in the import options.
        /// </remarks>
        public static TabularErrorCode AmbiguousSheet { get; } =
            TabularErrorCode.Create("tabular.schema.ambiguousSheet");

        /// <summary>Gets <c>tabular.schema.formatMismatch</c>: the content is not of the expected format.</summary>
        /// <remarks>
        /// Kind <see cref="TabularErrorKind.Data" />. The detected Excel variant differs from the expected one, a CSV
        /// import received a ZIP or compound file, or an Excel import received no container. Ask for a file of the
        /// expected format, or let the import detect the format.
        /// </remarks>
        public static TabularErrorCode FormatMismatch { get; } =
            TabularErrorCode.Create("tabular.schema.formatMismatch");

        /// <summary>
        /// Gets <c>tabular.schema.unsupportedFormat</c>: the file is in a format the library does not read.
        /// </summary>
        /// <remarks>
        /// Kind <see cref="TabularErrorKind.Data" />. The content is none of the supported formats, including legacy
        /// XLS, XLSB and password-protected workbooks. Ask for CSV, XLSX or XLSM without a password.
        /// </remarks>
        public static TabularErrorCode UnsupportedFormat { get; } =
            TabularErrorCode.Create("tabular.schema.unsupportedFormat");

        /// <summary>Gets <c>tabular.schema.limitExceeded</c>: the data exceeds a limit of the Excel format.</summary>
        /// <remarks>
        /// Kind <see cref="TabularErrorKind.Configuration" /> when the layout shows it before writing, otherwise
        /// <see cref="TabularErrorKind.Data" />. Rows, columns or the text of a cell exceed what a worksheet holds.
        /// Split the data over several sheets or files, or export to CSV.
        /// </remarks>
        public static TabularErrorCode LimitExceeded { get; } = TabularErrorCode.Create("tabular.schema.limitExceeded");
    }

    /// <summary>Codes starting with <c>tabular.parsing.</c>.</summary>
    public static class Parsing
    {
        /// <summary>Gets <c>tabular.parsing.invalidSyntax</c>: the file cannot be read as its format.</summary>
        /// <remarks>
        /// Kind <see cref="TabularErrorKind.Data" />. CSV quoting is broken, or a workbook is damaged. Ask for a
        /// correctly written file; the location names the first broken record.
        /// </remarks>
        public static TabularErrorCode InvalidSyntax { get; } =
            TabularErrorCode.Create("tabular.parsing.invalidSyntax");

        /// <summary>Gets <c>tabular.parsing.invalidEncoding</c>: text cannot be encoded or decoded.</summary>
        /// <remarks>
        /// Kind <see cref="TabularErrorKind.Data" />. A CSV input holds bytes that are invalid in its encoding, or an
        /// export writes a character the chosen encoding cannot represent. Ask for a file in the configured encoding,
        /// or configure the encoding of the file.
        /// </remarks>
        public static TabularErrorCode InvalidEncoding { get; } =
            TabularErrorCode.Create("tabular.parsing.invalidEncoding");
    }

    /// <summary>Codes starting with <c>tabular.conversion.</c>.</summary>
    public static class Conversion
    {
        /// <summary>Gets <c>tabular.conversion.missingValue</c>: an empty value for a field that needs one.</summary>
        /// <remarks>
        /// Kind <see cref="TabularErrorKind.Data" />. A cell is empty and its target is not nullable. Ask for the
        /// value, or make the field nullable.
        /// </remarks>
        public static TabularErrorCode MissingValue { get; } =
            TabularErrorCode.Create("tabular.conversion.missingValue");

        /// <summary>
        /// Gets <c>tabular.conversion.invalidFormat</c>: a value does not match the type or format of its field.
        /// </summary>
        /// <remarks>
        /// Kind <see cref="TabularErrorKind.Data" />. The text does not parse as the type, format, enum or flags of
        /// the field, or a local time is ambiguous, does not exist, or lacks its time zone. Ask for a corrected value,
        /// or configure the accepted formats of the field.
        /// </remarks>
        public static TabularErrorCode InvalidFormat { get; } =
            TabularErrorCode.Create("tabular.conversion.invalidFormat");

        /// <summary>Gets <c>tabular.conversion.outOfRange</c>: a value lies outside the range of its field.</summary>
        /// <remarks>
        /// Kind <see cref="TabularErrorKind.Data" />. The value is too large or too small for the target type, or a
        /// flags value has bits no member declares. Ask for a value in range, or widen the target type.
        /// </remarks>
        public static TabularErrorCode OutOfRange { get; } = TabularErrorCode.Create("tabular.conversion.outOfRange");

        /// <summary>Gets <c>tabular.conversion.precisionLoss</c>: a value cannot be represented exactly.</summary>
        /// <remarks>
        /// Kind <see cref="TabularErrorKind.Data" />. The target cannot hold the value without rounding that the
        /// field does not allow, or a <see cref="DateOnly" /> field receives a time of day. Ask for an exact value,
        /// or allow rounding for the field.
        /// </remarks>
        public static TabularErrorCode PrecisionLoss { get; } =
            TabularErrorCode.Create("tabular.conversion.precisionLoss");

        /// <summary>
        /// Gets <c>tabular.conversion.unsupportedValue</c>: a cell holds a value the field cannot take.
        /// </summary>
        /// <remarks>
        /// Kind <see cref="TabularErrorKind.Data" />. The cell type is not allowed for the field, a formula has no
        /// cached result, the cell holds an Excel error value, or a number is NaN or infinite. Ask for a plain value;
        /// a workbook saved by Excel stores the results of its formulas.
        /// </remarks>
        public static TabularErrorCode UnsupportedValue { get; } =
            TabularErrorCode.Create("tabular.conversion.unsupportedValue");

        /// <summary>
        /// Gets <c>tabular.conversion.missingRepresentation</c>: an exported value has no text to write.
        /// </summary>
        /// <remarks>
        /// Kind <see cref="TabularErrorKind.Data" />. An enum value is not declared, or is zero without a member, or
        /// every field of a record exported to Excel is empty. Export declared enum values, or give the value a text.
        /// </remarks>
        public static TabularErrorCode MissingRepresentation { get; } =
            TabularErrorCode.Create("tabular.conversion.missingRepresentation");
    }

    /// <summary>Codes starting with <c>tabular.translation.</c>.</summary>
    public static class Translation
    {
        /// <summary>Gets <c>tabular.translation.missing</c>: a needed translation is missing.</summary>
        /// <remarks>
        /// Kind <see cref="TabularErrorKind.Configuration" />. Translations are passed, but a key the call needs has
        /// no entry and no allowed fallback: header keys are checked before the first write, value keys per record.
        /// Add the missing key to the translations.
        /// </remarks>
        public static TabularErrorCode Missing { get; } = TabularErrorCode.Create("tabular.translation.missing");
    }

    /// <summary>Codes starting with <c>tabular.validation.</c>.</summary>
    public static class Validation
    {
        /// <summary>Gets <c>tabular.validation.failed</c>: a rule of the map rejected a value or record.</summary>
        /// <remarks>
        /// Kind <see cref="TabularErrorKind.Data" />. A field rule or a rule across fields without its own code
        /// failed, or an import journal rejected a file that it already holds. Ask for corrected data; give rules
        /// their own codes to tell them apart.
        /// </remarks>
        public static TabularErrorCode Failed { get; } = TabularErrorCode.Create("tabular.validation.failed");
    }

    /// <summary>Codes starting with <c>tabular.key.</c>.</summary>
    public static class Key
    {
        /// <summary>Gets <c>tabular.key.incomplete</c>: a part of a key is empty.</summary>
        /// <remarks>
        /// Kind <see cref="TabularErrorKind.Data" />. A part of a source key or of a match key is
        /// <see langword="null" />. Ask for the missing key value.
        /// </remarks>
        public static TabularErrorCode Incomplete { get; } = TabularErrorCode.Create("tabular.key.incomplete");

        /// <summary>Gets <c>tabular.key.duplicate</c>: several records of a sheet have the same key.</summary>
        /// <remarks>
        /// Kind <see cref="TabularErrorKind.Data" />. The source key repeats; every occurrence is rejected, and the
        /// related locations name the others. Ask for a file with unique keys.
        /// </remarks>
        public static TabularErrorCode Duplicate { get; } = TabularErrorCode.Create("tabular.key.duplicate");
    }

    /// <summary>Codes starting with <c>tabular.lookup.</c>.</summary>
    public static class Lookup
    {
        /// <summary>Gets <c>tabular.lookup.notFound</c>: a lookup target found no entity for a record.</summary>
        /// <remarks>
        /// Kind <see cref="TabularErrorKind.Data" />. The database holds no entity with the key of the record. Ask
        /// for an existing key, or create the entity first.
        /// </remarks>
        public static TabularErrorCode NotFound { get; } = TabularErrorCode.Create("tabular.lookup.notFound");

        /// <summary>
        /// Gets <c>tabular.lookup.notUnique</c>: a match or lookup found several entities for one key.
        /// </summary>
        /// <remarks>
        /// Kind <see cref="TabularErrorKind.Technical" />. The key is declared unique, but the database returns more
        /// than one entity, for example because the column collation compares differently than the match. Make the
        /// key unique in the database, or align the collation with the match.
        /// </remarks>
        public static TabularErrorCode NotUnique { get; } = TabularErrorCode.Create("tabular.lookup.notUnique");
    }

    /// <summary>Codes starting with <c>tabular.callback.</c>.</summary>
    public static class Callback
    {
        /// <summary>Gets <c>tabular.callback.failed</c>: a function of the application threw an exception.</summary>
        /// <remarks>
        /// Kind <see cref="TabularErrorKind.Technical" />. A rule, constructor, factory, read or write function,
        /// create or update function, header normalization, comparer, export source or completion function threw an
        /// exception the application did not assign to a code, or a completion function exceeded its timeout. Fix
        /// the function, or assign its expected exceptions to a code.
        /// </remarks>
        public static TabularErrorCode Failed { get; } = TabularErrorCode.Create("tabular.callback.failed");

        /// <summary>
        /// Gets <c>tabular.callback.invalidResult</c>: a function of the application returned an invalid result.
        /// </summary>
        /// <remarks>
        /// Kind <see cref="TabularErrorKind.Technical" />. A function or a storage adapter returned a result the
        /// contract does not allow, for example <see langword="null" /> instead of a value or wrong metadata. Fix the
        /// function or the adapter.
        /// </remarks>
        public static TabularErrorCode InvalidResult { get; } =
            TabularErrorCode.Create("tabular.callback.invalidResult");
    }

    /// <summary>Codes starting with <c>tabular.persistence.</c>.</summary>
    public static class Persistence
    {
        /// <summary>
        /// Gets <c>tabular.persistence.conflict</c>: the stored state changed or contradicts the run.
        /// </summary>
        /// <remarks>
        /// Kind <see cref="TabularErrorKind.Data" /> when the row was reset safely, otherwise
        /// <see cref="TabularErrorKind.Technical" />. A concurrency conflict occurred, another run took over the
        /// journal claim, a resolution contradicts the commit evidence, or a stored file contradicts its journal
        /// entry. Read the current state and repeat the import or the resolution.
        /// </remarks>
        public static TabularErrorCode Conflict { get; } = TabularErrorCode.Create("tabular.persistence.conflict");

        /// <summary>Gets <c>tabular.persistence.writeFailed</c>: writing to the database failed.</summary>
        /// <remarks>
        /// Kind <see cref="TabularErrorKind.Technical" />. A write failed with an exception the database errors do
        /// not assign to a code, or the start or completion of a journal entry was not saved. A failed group commit
        /// is <see cref="CommitUnknown" /> instead. Check the database and its constraints, then repeat the import.
        /// </remarks>
        public static TabularErrorCode WriteFailed { get; } =
            TabularErrorCode.Create("tabular.persistence.writeFailed");

        /// <summary>
        /// Gets <c>tabular.persistence.operationFailed</c>: a database operation other than a write failed.
        /// </summary>
        /// <remarks>
        /// Kind <see cref="TabularErrorKind.Technical" />. A query, a transaction, a savepoint or the release of an own
        /// context failed. Check the connection and the database, then repeat the call.
        /// </remarks>
        public static TabularErrorCode OperationFailed { get; } =
            TabularErrorCode.Create("tabular.persistence.operationFailed");

        /// <summary>Gets <c>tabular.persistence.commitUnknown</c>: whether a commit took effect is unknown.</summary>
        /// <remarks>
        /// Kind <see cref="TabularErrorKind.Technical" />. A commit failed, and no provider-independent signal tells
        /// a rejected commit from one that failed after the data was stored; the rows are counted as uncertain and
        /// the run ends. Clarify the stored state before a retry: with an import journal, recover the import from
        /// its commit evidence.
        /// </remarks>
        public static TabularErrorCode CommitUnknown { get; } =
            TabularErrorCode.Create("tabular.persistence.commitUnknown");

        /// <summary>
        /// Gets <c>tabular.persistence.rollbackUnknown</c>: whether a rollback took effect is unknown.
        /// </summary>
        /// <remarks>
        /// Kind <see cref="TabularErrorKind.Technical" />. A reset to a savepoint or a rollback was not confirmed, and
        /// the run ends. Clarify the stored state before a retry, and discard a context the application passed in.
        /// </remarks>
        public static TabularErrorCode RollbackUnknown { get; } =
            TabularErrorCode.Create("tabular.persistence.rollbackUnknown");

        /// <summary>
        /// Gets <c>tabular.persistence.contextRestoreFailed</c>: the EF Core state of a rejected row could not be
        /// reset.
        /// </summary>
        /// <remarks>
        /// Kind <see cref="TabularErrorKind.Technical" />. After a row error, the change tracker could not be returned
        /// to its state before the row, and the run ends. Discard the context and repeat the import with a new one.
        /// </remarks>
        public static TabularErrorCode ContextRestoreFailed { get; } =
            TabularErrorCode.Create("tabular.persistence.contextRestoreFailed");
    }

    /// <summary>Codes starting with <c>tabular.io.</c>.</summary>
    public static class Io
    {
        /// <summary>Gets <c>tabular.io.failed</c>: reading or writing a stream or file failed.</summary>
        /// <remarks>
        /// Kind <see cref="TabularErrorKind.Technical" />. Opening, reading, writing, publishing or removing a stream
        /// or file failed, or a storage adapter failed. Check the path, the permissions and the free space, then
        /// repeat the call.
        /// </remarks>
        public static TabularErrorCode Failed { get; } = TabularErrorCode.Create("tabular.io.failed");
    }

    /// <summary>Codes starting with <c>tabular.resource.</c>.</summary>
    public static class Resource
    {
        /// <summary>Gets <c>tabular.resource.limitExceeded</c>: a configured resource limit was reached.</summary>
        /// <remarks>
        /// Kind <see cref="TabularErrorKind.Technical" />. A limit of the format options was reached, for example the
        /// bytes read, the characters of a field, the bytes unpacked from a workbook or the temporary bytes. Raise
        /// the limit if the input is trusted, otherwise reject the input.
        /// </remarks>
        public static TabularErrorCode LimitExceeded { get; } =
            TabularErrorCode.Create("tabular.resource.limitExceeded");
    }
}
