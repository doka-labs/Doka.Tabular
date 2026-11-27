namespace Doka.Tabular.Samples.MultiSheet;

public sealed class Customer
{
    public long Id { get; init; }

    public int Number { get; init; }

    public string Name { get; init; } = string.Empty;
}

public sealed class Order
{
    public long Id { get; init; }

    public int Number { get; init; }

    public decimal Total { get; init; }
}

public sealed class AppDbContext : DbContext
{
    public AppDbContext(
        DbContextOptions<AppDbContext> options
    )
        : base(options) { }

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder
            .Entity<Customer>()
            .HasIndex(customer => customer.Number)
            .IsUnique();

        modelBuilder
            .Entity<Customer>()
            .Property(customer => customer.Name)
            .HasMaxLength(80);

        modelBuilder
            .Entity<Order>()
            .HasIndex(order => order.Number)
            .IsUnique();

        modelBuilder
            .Entity<Order>()
            .Property(order => order.Total)
            .HasPrecision(18, 2);
    }
}

public sealed record BookingRow(
    DateOnly Date,
    string Category,
    decimal? Amount,
    DateTime CreatedAt
);

public sealed record MonthRow(
    DateOnly Month,
    decimal Income,
    decimal Expenses
);

public sealed record CustomerDto(
    int Number,
    string Name
);

public sealed record OrderDto(
    int Number,
    decimal Total
);

/// <summary>
/// A formatted annual report with two sheets, then an import of two sheets into two EF targets, once in one
/// transaction over the workbook and once per sheet.
/// </summary>
public static class MultiSheetSample
{
    private const string Euro = "#,##0.00 [$EUR];[Red]-#,##0.00 [$EUR]";

    private static readonly ExcelLayoutOptions s_report = new()
    {
        Mode = ExcelLayoutMode.Table,
        TableStyle = ExcelTableStyle.Medium2,
        BandedRows = true,
        HeaderFill = ExcelColor.Create("#3A5A8C"),
        HeaderFontColor = ExcelColor.Create("#FFFFFF"),
        HeaderBold = true,
        HeaderVerticalAlignment = ExcelVerticalAlignment.Center,
        HeaderHeight = 22,
        FreezeHeader = ExcelHeaderFeature.Enabled,
    };

    private static readonly TabularMap<BookingRow> s_bookings = TabularMap
        .For<BookingRow>()
        .Column(
            x => x.Date,
            index: 0,
            key: ColumnKey.Create("date"),
            configure: c => c
                .Header("Datum")
                .ExcelNumberFormat("dd.mm.yyyy"))
        .Column(x => x.Category, index: 1, key: ColumnKey.Create("category"), configure: c => c.Header("Kategorie"))
        .Column(
            x => x.Amount,
            index: 2,
            key: ColumnKey.Create("amount"),
            configure: c => c
                .Header("Betrag")
                .ExcelNumberFormat(Euro))
        .Column(
            x => x.CreatedAt,
            index: 3,
            key: ColumnKey.Create("createdAt"),
            configure: c => c
                .Header("Erfasst")
                .ExcelNumberFormat("dd.mm.yyyy \" - \" hh:mm:ss"))
        .Build();

    private static readonly TabularMap<MonthRow> s_months = TabularMap
        .For<MonthRow>()
        .Column(
            x => x.Month,
            index: 0,
            key: ColumnKey.Create("month"),
            configure: c => c
                .Header("Monat")
                .ExcelNumberFormat("mmmm yyyy"))
        .Column(
            x => x.Income,
            index: 1,
            key: ColumnKey.Create("income"),
            configure: c => c
                .Header("Einnahmen")
                .ExcelNumberFormat(Euro))
        .Column(
            x => x.Expenses,
            index: 2,
            key: ColumnKey.Create("expenses"),
            configure: c => c
                .Header("Ausgaben")
                .ExcelNumberFormat(Euro))
        .Build();

    private static readonly TabularMap<CustomerDto> s_customers = TabularMap
        .For<CustomerDto>()
        .Column(x => x.Number, index: 0, configure: c => c.Header("Nummer"))
        .Column(
            x => x.Name,
            index: 1,
            configure: c => c
                .Header("Name")
                .Required())
        .Build();

    private static readonly TabularMap<OrderDto> s_orders = TabularMap
        .For<OrderDto>()
        .Column(x => x.Number, index: 0, configure: c => c.Header("Nummer"))
        .Column(
            x => x.Total,
            index: 1,
            configure: c => c
                .Header("Summe")
                .Validate(total => total > 0))
        .Build();

    public static async Task RunAsync(
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        var excel = new ExcelFormat(new ExcelFormatOptions());
        await ExportAnnualReportAsync(excel, output, cancellationToken);
        await using var database =
            await SampleDatabase<AppDbContext>.StartAsync(
                options => new AppDbContext(options),
                null,
                cancellationToken);

        IDbContextFactory<AppDbContext> factory =
            new SampleContextFactory<AppDbContext>(database.Options, options => new AppDbContext(options));

        var input = await WorkbookAsync(cancellationToken);
        foreach (var perSheet in new[] { false, true })
        {
            await ImportWorkbookAsync(excel, factory, input, perSheet, output, cancellationToken);
        }
    }

    private static async Task ExportAnnualReportAsync(
        ExcelFormat excel,
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        // Sheet order is registration order; every source is enumerated once.
        var report = AnnualReport();

        var (exported, file) = await excel.ExportAsync(report, cancellationToken);
        await using (file)
        {
            await output.WriteLineAsync(
                $"Annual report: {exported.Status}, {exported.RowsWritten} rows, {file?.Length} bytes");
        }
    }

    /// <summary>Two sheets with their own tables and column layouts, in registration order.</summary>
    private static ExcelWorkbookExport AnnualReport() => ExcelExport
        .Create()
        .Add(
            TabularSheetName.Create("Buchungen"),
            Bookings(),
            s_bookings,
            new ExcelSheetExportOptions
            {
                WriteHeader = true,
                Layout = s_report with
                {
                    TableName = ExcelTableName.Create("Buchungen"),
                    Columns = new Dictionary<ColumnKey, ExcelColumnLayout>
                    {
                        [ColumnKey.Create("category")] = new() { Width = 30 },
                        [ColumnKey.Create("amount")] = new()
                        {
                            Width = 16,
                            HorizontalAlignment = ExcelHorizontalAlignment.Right,
                        },
                    },
                },
            })
        .Add(
            TabularSheetName.Create("Monate"),
            new[]
            {
                new MonthRow(new DateOnly(2026, 1, 1), 4200m, 3100.5m),
                new MonthRow(new DateOnly(2026, 2, 1), 4200m, 2980m),
            },
            s_months,
            new ExcelSheetExportOptions
            {
                WriteHeader = true,
                Layout = s_report with { TableName = ExcelTableName.Create("Monate") },
            })
        .Build();

    /// <summary>Imports customers and orders strictly, in one transaction or in one transaction per sheet.</summary>
    private static async Task ImportWorkbookAsync(
        ExcelFormat excel,
        IDbContextFactory<AppDbContext> factory,
        MemoryStream input,
        bool perSheet,
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        var db = TabularDatabase.ForFactory(factory);
        var options = new TabularWriteOptions { TransactionPerSheet = perSheet };
        var customers = db
            .PrepareInsert<CustomerDto, Customer>(
                set: context => context.Customers,
                create: dto => new Customer
                {
                    Number = dto.Number,
                    Name = dto.Name,
                },
                match: TabularMatch
                    .For<CustomerDto, Customer>(s_customers)
                    .Part(dto => dto.Number, customer => customer.Number)
                    .UseUniqueKey()
                    .Build())
            .WithOptions(options);

        var orders = db
            .PrepareInsert<OrderDto, Order>(
                set: context => context.Orders,
                create: dto => new Order
                {
                    Number = dto.Number,
                    Total = dto.Total,
                })
            .WithOptions(options);

        var definition = ExcelImport.Create();
        var customerSheet = definition.Add(
            TabularSheetName.Create("Kunden"),
            s_customers,
            customers,
            new ExcelSheetImportOptions { Binding = ColumnBinding.ByHeaderName });

        var orderSheet = definition.Add(
            TabularSheetName.Create("Bestellungen"),
            s_orders,
            orders,
            new ExcelSheetImportOptions { Binding = ColumnBinding.ByHeaderName });

        input.Position = 0;
        var imported = await excel.ImportAsync(
            input,
            definition.Build(),
            new ExcelWorkbookImportOptions { Strategy = ImportStrategy.Strict },
            cancellationToken);

        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        await output.WriteLineAsync(
            $"{(perSheet ? "Per sheet" : "One transaction")}: {imported.Status}; Kunden {imported.Get(customerSheet).Status} ({imported.Get(customerSheet).RowsImported}), "
            + $"Bestellungen {imported.Get(orderSheet).Status}; stored customers {await context.Customers.CountAsync(cancellationToken)}, orders {await context.Orders.CountAsync(cancellationToken)}");
    }

    private static async IAsyncEnumerable<BookingRow> Bookings()
    {
        for (var day = 1; day <= 3; day++)
        {
            await Task.Yield();

            yield return new BookingRow(
                new DateOnly(2026, 1, day),
                day == 2
                    ? "Miete"
                    : "Einkauf",
                day == 3
                    ? null
                    : -12.5m * day,
                new DateTime(2026, 1, day, 9, 30, 0));
        }
    }

    /// <summary>The input workbook: customers, an order with an invalid total, and a sheet the import does not use.</summary>
    private static async Task<MemoryStream> WorkbookAsync(
        CancellationToken cancellationToken
    )
    {
        var excel = new ExcelFormat(new ExcelFormatOptions());
        var workbook = ExcelExport
            .Create()
            .Add(
                TabularSheetName.Create("Hinweise"),
                new[] { new CustomerDto(0, "Diese Mappe enthält Beispieldaten.") },
                s_customers)
            .Add(
                TabularSheetName.Create("Bestellungen"),
                new[] { new OrderDto(1, 99m), new OrderDto(2, -1m) },
                s_orders,
                new ExcelSheetExportOptions { WriteHeader = true })
            .Add(
                TabularSheetName.Create("Kunden"),
                new[] { new CustomerDto(1, "Ada"), new CustomerDto(2, "Grace") },
                s_customers,
                new ExcelSheetExportOptions { WriteHeader = true })
            .Build();

        var (_, file) = await excel.ExportAsync(workbook, cancellationToken);
        var copy = new MemoryStream();
        await using (file)
        {
            await file!.CopyToAsync(copy, cancellationToken);
        }

        return copy;
    }
}
