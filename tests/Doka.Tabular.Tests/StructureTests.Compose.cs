using static Doka.Tabular.Tests.Infrastructure.TestFiles;

namespace Doka.Tabular.Tests;

public sealed partial class StructureTests
{
    private static readonly Payment[] s_euroPayment = [new("a@example.org", new Money(12.34m, "EUR"))];

    private static readonly Payment[] s_sixEuroThenXyz =
    [
        .. Enumerable
            .Range(1, 7)
            .Select(number => new Payment(
                $"u{number}",
                new Money(
                    1,
                    number == 7
                        ? "XYZ"
                        : "EUR"))),
    ];

    private static readonly TabularMap<Taxed> s_taxed = TabularMap
        .For<Taxed>()
        .Compose(
            x => x.Total,
            c => c.Field<decimal>(index: 3, key: ColumnKey.Create("taxAmount")),
            c => c.Field<decimal>(index: 1, key: ColumnKey.Create("netAmount")),
            read: (tax, net) => new Money(net + tax, "EUR"),
            write: total => (19m, total.Amount - 19m))
        .Build();

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task Compose_ReadsItsParts(
        TabularType type
    )
    {
        // Act
        var report = await Import.ImportAsync(
            Create(type, ["a@example.org", "", "", 1234, "EUR"]),
            type,
            CentsPayment(),
            Token);

        // Assert
        Assert.Equal(
            new Money(12.34m, "EUR"),
            Assert.Single(report.Items!)
                .Amount);
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task Compose_WritesItsParts(
        TabularType type
    )
    {
        // Act
        var (_, file) = await Export.ExportAsync(s_euroPayment, CentsPayment(), type, Token);

        // Assert
        Assert.Equal(["1234", "EUR"], Cells(type, file!)[0][3..]);
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task Compose_WritesASingleSelectedPart(
        TabularType type
    )
    {
        // Act
        var (_, file) = await Export.ExportAsync(
            s_euroPayment,
            CentsPayment(),
            type,
            new TabularExportOptions { ColumnKeys = [ColumnKey.Create("currency")] },
            Token);

        // Assert
        Assert.Equal([["EUR"]], Cells(type, file!));
    }

    private static TabularMap<Payment> CentsPayment()
        => PaymentBuilder((minor, currency) => new Money(minor / 100m, currency))
            .Build();

    [Fact]
    public async Task Compose_SkipsReadWhenAPartFailsAndReportsOnlyFieldErrors()
    {
        // Arrange
        var calls = 0;
        var map = PaymentBuilder((minor, currency) =>
            {
                calls++;

                return new Money(minor / 100m, currency);
            })
            .Build();

        // Act
        var report = await CsvInstance.ImportAsync(
            TestInput.Utf8("no-mail,,,abc,EUR\nb@example.org,,,100,EUR"),
            map,
            new CsvImportOptions { Strategy = ImportStrategy.Partial },
            Token);

        // Assert
        Assert.Equal(2, report.Errors.Count);
        Assert.Equal(1, calls);
        Assert.Equal(["b@example.org"], report.Items!.Select(item => item.Email));
    }

    [Fact]
    public async Task Compose_AssignedReadExceptionNamesThePropertyWithoutColumn()
    {
        // Arrange
        var map = PaymentBuilder((minor, currency) => currency == "XYZ"
                ? throw new UnsupportedCurrencyException()
                : new Money(minor / 100m, currency))
            .Expect<UnsupportedCurrencyException>(TabularErrorCode.Create("currency.unsupported"))
            .Build();

        var lines = string.Join(
            "\n",
            [
                "E-Mail,Währung,x,Betrag", .. Enumerable
                    .Range(1, 6)
                    .Select(number => number == 6
                        ? "f@example.org,XYZ,,100"
                        : $"u{number}@example.org,EUR,,100"),
            ]);

        // Act
        var report = await CsvInstance.ImportAsync(
            TestInput.Utf8(lines),
            map,
            new CsvImportOptions
            {
                Binding = ColumnBinding.ByHeaderName,
                Strategy = ImportStrategy.Partial,
            },
            Token);

        // Assert
        var error = Assert.Single(report.Errors);
        Assert.Equal(
            ("currency.unsupported", 7L, "Amount"),
            (error.Code.Value, error.Location.RowNumber!.Value, error.Location.PropertyPath!.Value));

        Assert.Null(error.Location.ColumnIndex);
        Assert.Null(error.Location.ColumnKey);
    }

    [Theory]
    [InlineData("part1")]
    [InlineData("part3")]
    [InlineData("part4")]
    public void Compose_MissingPartDeclaration_IsAnArgumentErrorInEveryOverload(
        string part
    )
    {
        // Arrange
        Func<TabularComposeFields, TabularComposeField<long>> minor = c
            => c.Field<long>(index: 1, key: ColumnKey.Create("minor"));

        Func<TabularComposeFields, TabularComposeField<long>> missing = null!;

        // Act
        var exception = Record.Exception(() => part switch
        {
            "part1" => TabularMap
                .For<Payment>()
                .Compose(x => x.Amount, missing, minor, (a, _) => new Money(a, ""), _ => (0L, 0L)),
            "part3" => TabularMap
                .For<Payment>()
                .Compose(x => x.Amount, minor, minor, missing, (a, _, _) => new Money(a, ""), _ => (0L, 0L, 0L)),
            _ => TabularMap
                .For<Payment>()
                .Compose(
                    x => x.Amount,
                    minor,
                    minor,
                    minor,
                    missing,
                    (a, _, _, _) => new Money(a, ""),
                    _ => (0L, 0L, 0L, 0L)),
        });

        // Assert
        Assert.Equal(
            part,
            Assert.IsType<ArgumentNullException>(exception)
                .ParamName);
    }

    [Fact]
    public void Compose_PartDeclarationReturningNull_IsAnArgumentError()
    {
        // Arrange
        Func<TabularComposeFields, TabularComposeField<long>> minor = c
            => c.Field<long>(index: 1, key: ColumnKey.Create("minor"));

        Func<TabularComposeFields, TabularComposeField<long>> empty = _ => null!;

        // Act
        var exception = Record.Exception(() => TabularMap
            .For<Payment>()
            .Compose(x => x.Amount, minor, empty, (a, _) => new Money(a, ""), _ => (0L, 0L)));

        // Assert
        Assert.Equal(
            "part2",
            Assert.IsType<ArgumentException>(exception)
                .ParamName);
    }

    [Fact]
    public async Task Compose_WriteExceptionEndsTheExport()
    {
        // Arrange
        var map = TabularMap
            .For<Payment>()
            .Column(x => x.Email, index: 0)
            .Compose(
                x => x.Amount,
                c => c.Field<long>(index: 1, key: ColumnKey.Create("amountMinor")),
                c => c.Field<string>(index: 2, key: ColumnKey.Create("currency")),
                read: (minor, currency) => new Money(minor, currency),
                write: money => money.Currency == "XYZ"
                    ? throw new InvalidOperationException()
                    : ((long)money.Amount, money.Currency))
            .Build();

        // Act
        var report = await CsvInstance.ExportAsync(s_sixEuroThenXyz, map, new MemoryStream(), Token);

        // Assert
        Assert.Equal(
            (TabularErrorCodes.Callback.Failed, 7L, "Amount"),
            (report.FatalError!.Code, report.FatalError.Location.RowNumber!.Value,
                report.FatalError.Location.PropertyPath!.Value));
    }

    [Fact]
    public async Task Compose_ExportRuleRunsBeforeTheWrite()
    {
        // Arrange
        var validated = TabularMap
            .For<Payment>()
            .Column(x => x.Email, index: 0)
            .Compose(
                x => x.Amount,
                c => c.Field<long>(index: 1, key: ColumnKey.Create("amountMinor")),
                c => c.Field<string>(index: 2, key: ColumnKey.Create("currency")),
                read: (minor, currency) => new Money(minor, currency),
                write: money => money.Currency == "XYZ"
                    ? throw new InvalidOperationException()
                    : ((long)money.Amount, money.Currency))
            .Validate(
                x => x.Email,
                x => x.Amount,
                (_, amount) => amount.Currency != "XYZ",
                code: TabularErrorCode.Create("amount.currency"),
                alsoOnExport: true)
            .Build();

        // Act
        var report = await CsvInstance.ExportAsync(s_sixEuroThenXyz, validated, new MemoryStream(), Token);

        // Assert
        Assert.Equal("amount.currency", (report.FatalError ?? report.Errors[0]).Code.Value);
    }

    [Fact]
    public async Task Compose_NullForAPropertyThatIsNotNullable_IsAnInvalidResult()
    {
        // Arrange
        var map = PaymentBuilder((_, _) => null!)
            .Build();

        // Act
        var report = await CsvInstance.ImportAsync(
            TestInput.Utf8("a@example.org,,,1,EUR"),
            map,
            new CsvImportOptions { Strategy = ImportStrategy.Partial },
            Token);

        // Assert
        Assert.Equal(
            (TabularErrorCodes.Callback.InvalidResult, TabularErrorKind.Technical, "Amount"),
            (report.FatalError?.Code, report.FatalError?.Kind, report.FatalError?.Location.PropertyPath?.Value));

        Assert.Empty(report.Items!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ComposeErrorBehindAMultilineValue_NamesThePhysicalLineOfThePart(
        bool throwingWrite
    )
    {
        // Arrange
        var map = TabularMap
            .For<Payment>()
            .Column(x => x.Email, index: 0)
            .Compose(
                x => x.Amount,
                c => c.Field<long>(
                    index: 1,
                    key: ColumnKey.Create("amountMinor"),
                    configure: f => f.Validate(minor => minor > 0, alsoOnExport: true)),
                c => c.Field<string>(index: 2, key: ColumnKey.Create("currency")),
                read: (minor, currency) => new Money(minor, currency),
                write: money => throwingWrite
                    ? throw new InvalidOperationException("write failed")
                    : ((long)money.Amount, money.Currency))
            .Build();

        // Act
        var report = await CsvInstance.ExportAsync(
            new[] { new Payment("a\nb", new Money(-1, "EUR")) },
            map,
            new MemoryStream(),
            Token);

        // Assert
        Assert.Equal(2L, report.FatalError!.Location.RowNumber);
    }

    [Fact]
    public async Task Compose_NullableResult_IsNullForEmptyParts()
    {
        // Arrange
        var optional = TabularMap
            .For<OptionalPayment>()
            .Column(x => x.Email, index: 0)
            .Compose(
                x => x.Amount,
                c => c.Field<long?>(index: 1, key: ColumnKey.Create("amountMinor")),
                c => c.Field<string>(index: 2, key: ColumnKey.Create("currency")),
                read: (minor, currency) => minor is null
                    ? null
                    : new Money(minor.Value, currency),
                write: money => (money is null
                    ? null
                    : (long)money.Amount, money?.Currency ?? ""))
            .Build();

        // Act
        var report = await CsvInstance.ImportAsync(TestInput.Utf8("a@example.org,,"), optional, Token);

        // Assert
        Assert.Null(
            Assert.Single(report.Items!)
                .Amount);
    }

    [Fact]
    public async Task Compose_ReadsThePartsInTheirDeclarationOrder()
    {
        // Act
        var report = await CsvInstance.ImportAsync(TestInput.Utf8("x,100,x,19"), s_taxed, Token);

        // Assert
        Assert.Equal(
            119m,
            Assert.Single(report.Items!)
                .Total.Amount);
    }

    [Fact]
    public async Task Compose_WritesThePartsInTheirDeclarationOrder()
    {
        // Act
        var (_, file) = await CsvInstance.ExportAsync(new[] { new Taxed(new Money(119m, "EUR")) }, s_taxed, Token);

        // Assert
        Assert.Equal(["", "100", "", "19"], Cells(TabularType.Csv, file!)[0]);
    }
}
