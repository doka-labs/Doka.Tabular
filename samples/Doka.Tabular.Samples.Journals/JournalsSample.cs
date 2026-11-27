using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;

namespace Doka.Tabular.Samples.Journals;

public sealed class Customer
{
    public long Id { get; init; }

    public int Number { get; init; }

    public string Name { get; init; } = string.Empty;
}

public sealed class AppDbContext : DbContext
{
    public AppDbContext(
        DbContextOptions<AppDbContext> options
    )
        : base(options) { }

    public DbSet<Customer> Customers => Set<Customer>();

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
    }
}

public sealed record CustomerDto(
    int Number,
    string Name
);

/// <summary>A storage adapter in a directory; it uses the export id as file id, so deletion can find the file.</summary>
public sealed class DirectoryFileStore : ITabularFileStore
{
    private readonly string _directory;

    public DirectoryFileStore(
        string directory
    )
    {
        _directory = directory;
    }

    public async Task<TabularStoredFile> SaveAsync(
        TabularFileWriteRequest request,
        Stream content,
        CancellationToken cancellationToken
    )
    {
        await using var target = File.Create(PathFor(request.ExportId));
        await content.CopyToAsync(target, cancellationToken);

        return new TabularStoredFile(request.ExportId, request.ExportId, request.ByteSize, request.Sha256);
    }

    public Task DeleteAsync(
        Guid exportId,
        CancellationToken cancellationToken
    )
    {
        // File.Delete succeeds for a missing file, which the contract requires.
        File.Delete(PathFor(exportId));

        return Task.CompletedTask;
    }

    /// <summary>Opens a stored file for a download; the caller disposes the stream.</summary>
    public Stream OpenRead(
        Guid fileId
    ) => File.OpenRead(PathFor(fileId));

    private string PathFor(
        Guid id
    ) => Path.Combine(_directory, id.ToString("N"));
}

/// <summary>Stands in for a connection lost during the commit: the server rolls back, the import cannot know it.</summary>
public sealed class LoseConnectionOnCommit : DbTransactionInterceptor
{
    public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
        DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default
    )
    {
        await transaction.Connection!.CloseAsync();

        throw new ConnectionLostException();
    }
}

public sealed class ConnectionLostException : DbException
{
    public ConnectionLostException()
        : base("The connection was lost during the commit.") { }
}

/// <summary>
/// The optional journals: an import protected against duplicate files, clearing an unresolved import, a stored export
/// with retention and a later download, and the cleanup a scheduler of the application would call.
/// </summary>
public static class JournalsSample
{
    private static readonly TabularMap<CustomerDto> s_map = TabularMap
        .For<CustomerDto>()
        .Column(x => x.Number, index: 0, configure: c => c.Header("Nummer"))
        .Column(x => x.Name, index: 1, configure: c => c.Header("Name"))
        .Build();

    public static async Task RunAsync(
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        // The standard journal is registered through the context options; the tables come from the application's
        // migrations, here from EnsureCreated.
        await using var database = await SampleDatabase<AppDbContext>.StartAsync(
            options => new AppDbContext(options),
            options => options.UseTabularJournal(journal => journal.TablePrefix = "App_"),
            cancellationToken);

        IDbContextFactory<AppDbContext> factory =
            new SampleContextFactory<AppDbContext>(database.Options, options => new AppDbContext(options));

        var time = new FakeTimeProvider(
            new DateTimeOffset(
                2026,
                11,
                17,
                8,
                0,
                0,
                TimeSpan.Zero));

        var csv = new CsvFormat(new CsvFormatOptions { TimeProvider = time });
        var journal = TabularJournal
            .ForFactory(factory)
            .WithOptions(new TabularJournalOptions { TimeProvider = time });

        await ImportTwiceAsync(csv, journal, factory, output, cancellationToken);
        await ClearUncertainImportAsync(csv, journal, database.Options, output, cancellationToken);
        await StoreExportAsync(csv, journal, factory, time, output, cancellationToken);
    }

    /// <summary>The same bytes in the same scope are imported once; a renamed copy is skipped without reading or writing.</summary>
    private static async Task ImportTwiceAsync(
        CsvFormat csv,
        TabularJournal journal,
        IDbContextFactory<AppDbContext> factory,
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        var completed = new List<string>();
        foreach (var fileName in new[] { "customers.csv", "customers-copy.csv" })
        {
            var destination = TabularDatabase
                .ForFactory(factory)
                .PrepareInsert<CustomerDto, Customer>(
                    set: context => context.Customers,
                    create: dto => new Customer
                    {
                        Number = dto.Number,
                        Name = dto.Name,
                    })
                .WithImportLog(
                    journal,
                    new TabularImportLogOptions
                    {
                        Scope = JournalScope.Create("customers"),
                        FileName = fileName,
                        DuplicateBehavior = ImportDuplicateBehavior.Skip,
                        OnCompletedAsync = (entry, _) =>
                        {
                            completed.Add($"{entry.Decision} {entry.Status}");

                            return Task.CompletedTask;
                        },
                    });

            // WHY: MemoryStream needs an array; the materialization stays explicit instead of a spread.
            // ReSharper disable once UseCollectionExpression
            var report = await csv.ImportAsync(
                new MemoryStream("1,Ada\n2,Grace"u8.ToArray()),
                s_map,
                destination,
                cancellationToken);

            await output.WriteLineAsync(
                $"{fileName}: {report.Status}, decision {report.Journal!.Decision}, existing {report.Journal.ExistingImportId is not null}");
        }

        await output.WriteLineAsync($"Completion function: {string.Join("; ", completed)}");
    }

    /// <summary>
    /// A commit with an unknown outcome leaves the claim unresolved. Recover reads the committed evidence; only when
    /// that is not enough the application records its own decision with a reason, here the rollback a DBA confirmed.
    /// </summary>
    private static async Task ClearUncertainImportAsync(
        CsvFormat csv,
        TabularJournal journal,
        DbContextOptions<AppDbContext> options,
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        var lossy = new SampleContextFactory<AppDbContext>(
            new DbContextOptionsBuilder<AppDbContext>(options)
                .AddInterceptors(new LoseConnectionOnCommit())
                .Options,
            created => new AppDbContext(created));

        var lost = await ImportAsync(csv, journal, lossy, cancellationToken);
        var importId = lost.Journal!.EntryId;
        var recovery = await journal.RecoverImportAsync(importId, cancellationToken);
        var factory = new SampleContextFactory<AppDbContext>(options, created => new AppDbContext(created));
        var blocked = await ImportAsync(csv, journal, factory, cancellationToken);
        var resolution = await journal.ResolveImportAsync(
            importId,
            new ImportResolution
            {
                Outcome = ImportResolutionOutcome.NoChanges,
                Reason = "Rollback confirmed by the DBA.",
            },
            cancellationToken);

        var retried = await ImportAsync(csv, journal, factory, cancellationToken);
        await output.WriteLineAsync(
            $"Uncertain import: {lost.FatalError?.Code.Value}; recover {recovery.State}; retry before resolve {blocked.Journal!.Decision}; resolve {resolution.Status}; retry {retried.Journal!.Decision} {retried.Status}");
    }

    private static Task<ImportReport<CustomerDto>> ImportAsync(
        CsvFormat csv,
        TabularJournal journal,
        IDbContextFactory<AppDbContext> factory,
        CancellationToken cancellationToken
    ) => csv.ImportAsync(
        new MemoryStream("3,Linus"u8.ToArray()),
        s_map,
        TabularDatabase
            .ForFactory(factory)
            .PrepareInsert<CustomerDto, Customer>(
                set: context => context.Customers,
                create: dto => new Customer
                {
                    Number = dto.Number,
                    Name = dto.Name,
                })
            .WithImportLog(journal, new TabularImportLogOptions { Scope = JournalScope.Create("customers") }),
        cancellationToken);

    /// <summary>Stored export with retention: downloadable until the expiry, then deleted by the cleanup.</summary>
    private static async Task StoreExportAsync(
        CsvFormat csv,
        TabularJournal journal,
        IDbContextFactory<AppDbContext> factory,
        FakeTimeProvider time,
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        var directory = Directory.CreateTempSubdirectory("tabular-exports-")
            .FullName;

        try
        {
            var store = new DirectoryFileStore(directory);
            Guid? exportId = null;
            var target = TabularFileTarget
                .To(store, fileName: "Kunden.csv")
                .WithRetention(TimeSpan.FromDays(7))
                .WithExportLog(
                    journal,
                    new TabularExportLogOptions
                    {
                        Scope = JournalScope.Create("customers"),
                        OnCompletedAsync = (completed, _) =>
                        {
                            // The application hands this id to the user, for example in a notification.
                            exportId = completed.ExportId;

                            return Task.CompletedTask;
                        },
                    });

            await ExportAsync(csv, factory, target, output, cancellationToken);
            await output.WriteLineAsync(
                $"Download now: {await DownloadAsync(journal, factory, store, exportId!.Value, time.GetUtcNow(), cancellationToken)}");

            time.Advance(TimeSpan.FromDays(7));
            await output.WriteLineAsync(
                $"Download after seven days: {await DownloadAsync(journal, factory, store, exportId.Value, time.GetUtcNow(), cancellationToken)}");

            var deleted = await journal.DeleteExpiredExportsAsync(store, cancellationToken);
            var logs = await journal.DeleteImportLogsAsync(time.GetUtcNow(), cancellationToken);
            await output.WriteLineAsync(
                $"Cleanup: {deleted.Count} expired export deleted, {logs.Count} import logs deleted, files left {Directory.GetFiles(directory).Length}");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task ExportAsync(
        CsvFormat csv,
        IDbContextFactory<AppDbContext> factory,
        TabularFileTarget target,
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var exported = await csv.ExportAsync(
            context
                .Customers
                .AsNoTracking()
                .OrderBy(customer => customer.Number)
                .Select(customer => new CustomerDto(customer.Number, customer.Name)),
            s_map,
            target,
            cancellationToken);

        await output.WriteLineAsync($"Stored export: {exported.Status}, file id set {exported.FileId is not null}");
    }

    /// <summary>
    /// A later download: availability is a plain query over the journal, ready and not yet expired; permissions,
    /// tenants, and routes belong to the application. The store opens the file by the confirmed file id.
    /// </summary>
    private static async Task<string> DownloadAsync(
        TabularJournal journal,
        IDbContextFactory<AppDbContext> factory,
        DirectoryFileStore store,
        Guid exportId,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var export = await journal
            .QueryExports(context)
            .Where(log => log.Id == exportId && log.Status == TabularExportLogStatus.Ready)
            .Where(log => log.ExpiresAt == null || log.ExpiresAt > now)
            .Select(log => new
            {
                log.FileId,
                log.FileName,
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (export?.FileId is not { } fileId)
        {
            return "not found";
        }

        await using var file = store.OpenRead(fileId);
        using var reader = new StreamReader(file);
        var text = await reader.ReadToEndAsync(cancellationToken);

        return $"{export.FileName} with {text.ReplaceLineEndings("|").TrimEnd('|')}";
    }
}
