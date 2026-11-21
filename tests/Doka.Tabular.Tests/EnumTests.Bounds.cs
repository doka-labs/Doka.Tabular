using static Doka.Tabular.Tests.Infrastructure.TestFiles;

namespace Doka.Tabular.Tests;

/// <summary>
/// The base type limits of the specification for every base type, in CSV and in Excel text cells: Min and Max read and
/// write exactly, the next number on either side is a range error, number forms are strict, a second name is read, and
/// a flags enum keeps its highest bit. Limits of more than 15 digits go to Excel as a text column only.
/// </summary>
public sealed partial class EnumTests
{
    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public enum BoundedByte : byte
    {
        Min = byte.MinValue,
        One = 1,
        Uno = One,
        Max = byte.MaxValue,
    }

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public enum BoundedInt16 : short
    {
        Min = short.MinValue,
        One = 1,
        Uno = One,
        Max = short.MaxValue,
    }

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public enum BoundedInt32
    {
        Min = int.MinValue,
        One = 1,
        Uno = One,
        Max = int.MaxValue,
    }

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public enum BoundedInt64 : long
    {
        Min = long.MinValue,
        One = 1,
        Uno = One,
        Max = long.MaxValue,
    }

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public enum BoundedSByte : sbyte
    {
        Min = sbyte.MinValue,
        One = 1,
        Uno = One,
        Max = sbyte.MaxValue,
    }

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public enum BoundedUInt16 : ushort
    {
        Min = ushort.MinValue,
        One = 1,
        Uno = One,
        Max = ushort.MaxValue,
    }

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public enum BoundedUInt32 : uint
    {
        Min = uint.MinValue,
        One = 1,
        Uno = One,
        Max = uint.MaxValue,
    }

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public enum BoundedUInt64 : ulong
    {
        Min = ulong.MinValue,
        One = 1,
        Uno = One,
        Max = ulong.MaxValue,
    }

    [Flags]
    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public enum HighBitByte : byte
    {
        Low = 1,
        High = unchecked(byte.MaxValue / 2 + 1),
    }

    [Flags]
    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public enum HighBitInt16 : short
    {
        Low = 1,
        High = short.MinValue,
    }

    [Flags]
    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public enum HighBitInt32
    {
        Low = 1,
        High = int.MinValue,
    }

    [Flags]
    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public enum HighBitInt64 : long
    {
        Low = 1,
        High = long.MinValue,
    }

    [Flags]
    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public enum HighBitSByte : sbyte
    {
        Low = 1,
        High = sbyte.MinValue,
    }

    [Flags]
    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public enum HighBitUInt16 : ushort
    {
        Low = 1,
        High = unchecked(ushort.MaxValue / 2 + 1),
    }

    [Flags]
    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public enum HighBitUInt32 : uint
    {
        Low = 1,
        High = unchecked(uint.MaxValue / 2 + 1),
    }

    [Flags]
    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public enum HighBitUInt64 : ulong
    {
        Low = 1,
        High = unchecked(ulong.MaxValue / 2 + 1),
    }

    private static readonly Dictionary<string, BaseType> s_baseTypes = new()
    {
        ["SByte"] = new BaseType<BoundedSByte, HighBitSByte>("-128", "127", "-129", "128", "-128", wide: false),
        ["Byte"] = new BaseType<BoundedByte, HighBitByte>("0", "255", "-1", "256", "128", wide: false),
        ["Int16"] =
            new BaseType<BoundedInt16, HighBitInt16>("-32768", "32767", "-32769", "32768", "-32768", wide: false),
        ["UInt16"] = new BaseType<BoundedUInt16, HighBitUInt16>("0", "65535", "-1", "65536", "32768", wide: false),
        ["Int32"] = new BaseType<BoundedInt32, HighBitInt32>("-2147483648",
            "2147483647",
            "-2147483649",
            "2147483648",
            "-2147483648",
            wide: false),
        ["UInt32"] =
            new BaseType<BoundedUInt32, HighBitUInt32>("0",
                "4294967295",
                "-1",
                "4294967296",
                "2147483648",
                wide: false),
        ["Int64"] = new BaseType<BoundedInt64, HighBitInt64>("-9223372036854775808",
            "9223372036854775807",
            "-9223372036854775809",
            "9223372036854775808",
            "-9223372036854775808",
            wide: true),
        ["UInt64"] = new BaseType<BoundedUInt64, HighBitUInt64>("0",
            "18446744073709551615",
            "-1",
            "18446744073709551616",
            "9223372036854775808",
            wide: true),
    };

    public static TheoryData<string> BaseTypeNames() => new(s_baseTypes.Keys);

    public static TheoryData<string, TabularType> BaseTypes()
    {
        var data = new TheoryData<string, TabularType>();
        foreach (var baseType in s_baseTypes.Keys)
        {
            data.Add(baseType, TabularType.Csv);
            data.Add(baseType, TabularType.Excel);
        }

        return data;
    }

    public static TheoryData<string, TabularType, string> BaseTypesAndLimits() => BaseTypesWith("Min", "Max");

    public static TheoryData<string, TabularType, string> BaseTypesAndOutsideSides() => BaseTypesWith("below", "above");

    public static TheoryData<string, TabularType, string> BaseTypesAndOneForms() => BaseTypesWith("+0001", "Uno");

    public static TheoryData<string, TabularType, string> BaseTypesAndInvalidForms()
        => BaseTypesWith("1.0", "1e0", "0x1", "2");

    public static TheoryData<string, TabularType, string> BaseTypesAndFlags()
        => BaseTypesWith("High", "Low", "Low, High");

    [Theory]
    [MemberData(nameof(BaseTypesAndLimits))]
    public async Task BaseTypeLimit_IsReadExactly(
        string baseType,
        TabularType type,
        string limit
    )
    {
        // Arrange
        var bounds = s_baseTypes[baseType];

        // Act
        var outcome = await bounds.ReadAsync(type, bounds.Text(limit));

        // Assert
        Assert.Equal((ReportStatus.Succeeded, bounds.Member(limit)), (outcome.Status, outcome.Value));
    }

    [Theory]
    [MemberData(nameof(BaseTypesAndOutsideSides))]
    public async Task NumberNextToABaseTypeLimit_IsARangeError(
        string baseType,
        TabularType type,
        string side
    )
    {
        // Arrange
        var bounds = s_baseTypes[baseType];

        // Act
        var outcome = await bounds.ReadAsync(type, bounds.Text(side));

        // Assert
        Assert.Equal(TabularErrorCodes.Conversion.OutOfRange,
            Assert.Single(outcome.Errors)
                .Code);
    }

    [Theory]
    [MemberData(nameof(BaseTypesAndOneForms))]
    public async Task PaddedNumberAndSecondName_AreReadAsTheSameMember(
        string baseType,
        TabularType type,
        string text
    )
    {
        // Arrange
        var bounds = s_baseTypes[baseType];

        // Act
        var outcome = await bounds.ReadAsync(type, text);

        // Assert
        Assert.Equal(bounds.Member("One"), outcome.Value);
    }

    [Theory]
    [MemberData(nameof(BaseTypesAndInvalidForms))]
    public async Task NonPlainNumberForms_AreRejectedForEveryBaseType(
        string baseType,
        TabularType type,
        string text
    )
    {
        // Act
        var outcome = await s_baseTypes[baseType]
            .ReadAsync(type, text);

        // Assert
        Assert.Equal(ReportStatus.Failed, outcome.Status);
    }

    [Theory]
    [MemberData(nameof(BaseTypesAndLimits))]
    public async Task BaseTypeLimit_IsWrittenExactlyAsText(
        string baseType,
        TabularType type,
        string limit
    )
    {
        // Arrange
        var bounds = s_baseTypes[baseType];

        // Act
        var text = await bounds.ExportAsTextAsync(type, limit);

        // Assert
        Assert.Equal(bounds.Text(limit), text);
    }

    [Theory]
    [MemberData(nameof(BaseTypes))]
    public async Task BaseTypeLimitsOfMoreThan15Digits_AreNoNativeExcelNumbers(
        string baseType,
        TabularType type
    )
    {
        // Arrange
        var bounds = s_baseTypes[baseType];

        // Act
        var status = await bounds.ExportLimitsAsNumbersAsync(type);

        // Assert
        Assert.Equal(type == TabularType.Excel && bounds.Wide
                ? ReportStatus.Failed
                : ReportStatus.Succeeded,
            status);
    }

    [Theory]
    [MemberData(nameof(BaseTypesAndFlags))]
    public async Task FlagsOfEveryBaseType_KeepTheHighestBit(
        string baseType,
        TabularType type,
        string text
    )
    {
        // Arrange
        var bounds = s_baseTypes[baseType];

        // Act
        var outcome = await bounds.ReadFlagsAsync(type, text);

        // Assert
        Assert.Equal((ReportStatus.Succeeded, bounds.FlagsMember(text)), (outcome.Status, outcome.Value));
    }

    [Theory]
    [MemberData(nameof(BaseTypeNames))]
    public async Task HighestFlagsBit_IsWrittenAsTheNumberOfTheBaseType(
        string baseType
    )
    {
        // Arrange
        var bounds = s_baseTypes[baseType];

        // Act
        var text = await bounds.ExportHighBitAsync(TabularType.Csv);

        // Assert
        Assert.Equal(bounds.HighText, text);
    }

    private static TheoryData<string, TabularType, string> BaseTypesWith(
        params string[] cases
    )
    {
        var data = new TheoryData<string, TabularType, string>();
        foreach (var baseType in s_baseTypes.Keys)
        {
            foreach (var type in new[] { TabularType.Csv, TabularType.Excel })
            {
                foreach (var name in cases)
                {
                    data.Add(baseType, type, name);
                }
            }
        }

        return data;
    }

    /// <summary>The outcome of a read without its type: status, the single value, and the errors.</summary>
    private sealed record ReadOutcome(
        ReportStatus Status,
        object? Value,
        IReadOnlyList<TabularError> Errors
    );

    /// <summary>The limits of one base type, the texts the specification fixes, and the calls on its enums.</summary>
    private abstract class BaseType(
        string minText,
        string maxText,
        string belowText,
        string aboveText,
        string highText,
        bool wide
    )
    {
        public string HighText => highText;

        public bool Wide => wide;

        public string Text(
            string name
        ) => name switch
        {
            "Min" => minText,
            "Max" => maxText,
            "below" => belowText,
            _ => aboveText,
        };

        public abstract object Member(
            string name
        );

        public abstract object FlagsMember(
            string names
        );

        public abstract Task<ReadOutcome> ReadAsync(
            TabularType type,
            string text
        );

        public abstract Task<ReadOutcome> ReadFlagsAsync(
            TabularType type,
            string text
        );

        public abstract Task<string?> ExportAsTextAsync(
            TabularType type,
            string limit
        );

        public abstract Task<ReportStatus> ExportLimitsAsNumbersAsync(
            TabularType type
        );

        public abstract Task<string?> ExportHighBitAsync(
            TabularType type
        );
    }

    private sealed class BaseType<T, TFlags>(
        string minText,
        string maxText,
        string belowText,
        string aboveText,
        string highText,
        bool wide
    ) : BaseType(minText, maxText, belowText, aboveText, highText, wide)
        where T : struct, Enum
        where TFlags : struct, Enum
    {
        private static readonly TabularMap<Value<T>> s_numeric = Map<T>(c => c
            .AllowNumericInput()
            .ExportAsNumber());

        private static readonly TabularMap<Value<T>> s_text = Map<T>(c => c
            .AllowNumericInput()
            .ExportAsNumber()
            .ExcelAsText());

        private static readonly TabularMap<Value<TFlags>> s_flags = Map<TFlags>(c => c
            .AllowNumericInput()
            .ExportAsNumber());

        public override object Member(
            string name
        ) => Enum.Parse<T>(name);

        public override object FlagsMember(
            string names
        ) => Enum.Parse<TFlags>(names);

        public override async Task<ReadOutcome> ReadAsync(
            TabularType type,
            string text
        ) => Outcome(await EnumTests.ReadAsync(type, text, s_numeric));

        public override async Task<ReadOutcome> ReadFlagsAsync(
            TabularType type,
            string text
        ) => Outcome(await EnumTests.ReadAsync(type, text, s_flags));

        public override async Task<string?> ExportAsTextAsync(
            TabularType type,
            string limit
        ) => (await ExportTextAsync(type, Enum.Parse<T>(limit), s_text)).Text;

        public override async Task<ReportStatus> ExportLimitsAsNumbersAsync(
            TabularType type
        ) => (await Export.ExportAsync(
            new[] { new Value<T>(1, Enum.Parse<T>("Min")), new Value<T>(2, Enum.Parse<T>("Max")) },
            s_numeric,
            type,
            Token)).Report.Status;

        public override async Task<string?> ExportHighBitAsync(
            TabularType type
        ) => (await ExportTextAsync(type, Enum.Parse<TFlags>("High"), s_flags)).Text;

        private static ReadOutcome Outcome<TValue>(
            ImportReport<Value<TValue>> report
        ) => new(report.Status,
            report.Items is [var only]
                ? only.Item
                : null,
            report.Errors);
    }
}
