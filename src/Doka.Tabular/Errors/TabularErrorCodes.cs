namespace Doka.Tabular;

/// <summary>
/// The error codes of Doka.Tabular, grouped by the segment after <c>tabular.</c>; for example
/// <see cref="Schema.MissingColumn" /> is <c>tabular.schema.missingColumn</c>.
/// </summary>
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
        /// <summary>Gets <c>tabular.configuration.invalid</c>.</summary>
        public static TabularErrorCode Invalid { get; } = TabularErrorCode.Create("tabular.configuration.invalid");
    }

    /// <summary>Codes starting with <c>tabular.schema.</c>.</summary>
    public static class Schema
    {
        /// <summary>Gets <c>tabular.schema.missingColumn</c>.</summary>
        public static TabularErrorCode MissingColumn { get; } = TabularErrorCode.Create("tabular.schema.missingColumn");

        /// <summary>Gets <c>tabular.schema.ambiguousColumn</c>.</summary>
        public static TabularErrorCode AmbiguousColumn { get; } =
            TabularErrorCode.Create("tabular.schema.ambiguousColumn");

        /// <summary>Gets <c>tabular.schema.missingSheet</c>.</summary>
        public static TabularErrorCode MissingSheet { get; } = TabularErrorCode.Create("tabular.schema.missingSheet");

        /// <summary>Gets <c>tabular.schema.ambiguousSheet</c>.</summary>
        public static TabularErrorCode AmbiguousSheet { get; } =
            TabularErrorCode.Create("tabular.schema.ambiguousSheet");

        /// <summary>Gets <c>tabular.schema.formatMismatch</c>.</summary>
        public static TabularErrorCode FormatMismatch { get; } =
            TabularErrorCode.Create("tabular.schema.formatMismatch");

        /// <summary>Gets <c>tabular.schema.unsupportedFormat</c>.</summary>
        public static TabularErrorCode UnsupportedFormat { get; } =
            TabularErrorCode.Create("tabular.schema.unsupportedFormat");

        /// <summary>Gets <c>tabular.schema.limitExceeded</c>.</summary>
        public static TabularErrorCode LimitExceeded { get; } = TabularErrorCode.Create("tabular.schema.limitExceeded");
    }

    /// <summary>Codes starting with <c>tabular.parsing.</c>.</summary>
    public static class Parsing
    {
        /// <summary>Gets <c>tabular.parsing.invalidSyntax</c>.</summary>
        public static TabularErrorCode InvalidSyntax { get; } =
            TabularErrorCode.Create("tabular.parsing.invalidSyntax");

        /// <summary>Gets <c>tabular.parsing.invalidEncoding</c>.</summary>
        public static TabularErrorCode InvalidEncoding { get; } =
            TabularErrorCode.Create("tabular.parsing.invalidEncoding");
    }

    /// <summary>Codes starting with <c>tabular.conversion.</c>.</summary>
    public static class Conversion
    {
        /// <summary>Gets <c>tabular.conversion.missingValue</c>.</summary>
        public static TabularErrorCode MissingValue { get; } =
            TabularErrorCode.Create("tabular.conversion.missingValue");

        /// <summary>Gets <c>tabular.conversion.invalidFormat</c>.</summary>
        public static TabularErrorCode InvalidFormat { get; } =
            TabularErrorCode.Create("tabular.conversion.invalidFormat");

        /// <summary>Gets <c>tabular.conversion.outOfRange</c>.</summary>
        public static TabularErrorCode OutOfRange { get; } = TabularErrorCode.Create("tabular.conversion.outOfRange");

        /// <summary>Gets <c>tabular.conversion.precisionLoss</c>.</summary>
        public static TabularErrorCode PrecisionLoss { get; } =
            TabularErrorCode.Create("tabular.conversion.precisionLoss");

        /// <summary>Gets <c>tabular.conversion.unsupportedValue</c>.</summary>
        public static TabularErrorCode UnsupportedValue { get; } =
            TabularErrorCode.Create("tabular.conversion.unsupportedValue");

        /// <summary>Gets <c>tabular.conversion.missingRepresentation</c>.</summary>
        public static TabularErrorCode MissingRepresentation { get; } =
            TabularErrorCode.Create("tabular.conversion.missingRepresentation");
    }

    /// <summary>Codes starting with <c>tabular.translation.</c>.</summary>
    public static class Translation
    {
        /// <summary>Gets <c>tabular.translation.missing</c>.</summary>
        public static TabularErrorCode Missing { get; } = TabularErrorCode.Create("tabular.translation.missing");
    }

    /// <summary>Codes starting with <c>tabular.validation.</c>.</summary>
    public static class Validation
    {
        /// <summary>Gets <c>tabular.validation.failed</c>.</summary>
        public static TabularErrorCode Failed { get; } = TabularErrorCode.Create("tabular.validation.failed");
    }

    /// <summary>Codes starting with <c>tabular.key.</c>.</summary>
    public static class Key
    {
        /// <summary>Gets <c>tabular.key.incomplete</c>.</summary>
        public static TabularErrorCode Incomplete { get; } = TabularErrorCode.Create("tabular.key.incomplete");

        /// <summary>Gets <c>tabular.key.duplicate</c>.</summary>
        public static TabularErrorCode Duplicate { get; } = TabularErrorCode.Create("tabular.key.duplicate");
    }

    /// <summary>Codes starting with <c>tabular.lookup.</c>.</summary>
    public static class Lookup
    {
        /// <summary>Gets <c>tabular.lookup.notFound</c>.</summary>
        public static TabularErrorCode NotFound { get; } = TabularErrorCode.Create("tabular.lookup.notFound");

        /// <summary>Gets <c>tabular.lookup.notUnique</c>.</summary>
        public static TabularErrorCode NotUnique { get; } = TabularErrorCode.Create("tabular.lookup.notUnique");
    }

    /// <summary>Codes starting with <c>tabular.callback.</c>.</summary>
    public static class Callback
    {
        /// <summary>Gets <c>tabular.callback.failed</c>.</summary>
        public static TabularErrorCode Failed { get; } = TabularErrorCode.Create("tabular.callback.failed");

        /// <summary>Gets <c>tabular.callback.invalidResult</c>.</summary>
        public static TabularErrorCode InvalidResult { get; } =
            TabularErrorCode.Create("tabular.callback.invalidResult");
    }

    /// <summary>Codes starting with <c>tabular.persistence.</c>.</summary>
    public static class Persistence
    {
        /// <summary>Gets <c>tabular.persistence.conflict</c>.</summary>
        public static TabularErrorCode Conflict { get; } = TabularErrorCode.Create("tabular.persistence.conflict");

        /// <summary>Gets <c>tabular.persistence.writeFailed</c>.</summary>
        public static TabularErrorCode WriteFailed { get; } =
            TabularErrorCode.Create("tabular.persistence.writeFailed");

        /// <summary>Gets <c>tabular.persistence.operationFailed</c>.</summary>
        public static TabularErrorCode OperationFailed { get; } =
            TabularErrorCode.Create("tabular.persistence.operationFailed");

        /// <summary>Gets <c>tabular.persistence.commitUnknown</c>.</summary>
        public static TabularErrorCode CommitUnknown { get; } =
            TabularErrorCode.Create("tabular.persistence.commitUnknown");

        /// <summary>Gets <c>tabular.persistence.rollbackUnknown</c>.</summary>
        public static TabularErrorCode RollbackUnknown { get; } =
            TabularErrorCode.Create("tabular.persistence.rollbackUnknown");

        /// <summary>Gets <c>tabular.persistence.contextRestoreFailed</c>.</summary>
        public static TabularErrorCode ContextRestoreFailed { get; } =
            TabularErrorCode.Create("tabular.persistence.contextRestoreFailed");
    }

    /// <summary>Codes starting with <c>tabular.io.</c>.</summary>
    public static class Io
    {
        /// <summary>Gets <c>tabular.io.failed</c>.</summary>
        public static TabularErrorCode Failed { get; } = TabularErrorCode.Create("tabular.io.failed");
    }

    /// <summary>Codes starting with <c>tabular.resource.</c>.</summary>
    public static class Resource
    {
        /// <summary>Gets <c>tabular.resource.limitExceeded</c>.</summary>
        public static TabularErrorCode LimitExceeded { get; } =
            TabularErrorCode.Create("tabular.resource.limitExceeded");
    }
}
