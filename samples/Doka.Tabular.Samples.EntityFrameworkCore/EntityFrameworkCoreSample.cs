namespace Doka.Tabular.Samples.EntityFrameworkCore;

public sealed class User
{
    private User() { }

    public long Id { get; private init; }

    public int PersonnelNumber { get; private init; }

    public string Email { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public static User Create(
        int personnelNumber,
        string email,
        string name
    )
    {
        if (personnelNumber <= 0)
        {
            throw new InvalidUserException("The personnel number must be positive.");
        }

        return new User { PersonnelNumber = personnelNumber }.Change(email, name);
    }

    public User Change(
        string email,
        string name
    )
    {
        if (name.Length is 0 or > 80)
        {
            throw new InvalidUserException("The name has 1 to 80 characters.");
        }

        Email = email;
        Name = name;

        return this;
    }
}

public sealed class ImportRun
{
    public long Id { get; init; }

    public string State { get; init; } = string.Empty;
}

public sealed class InvalidUserException : Exception
{
    public InvalidUserException(
        string message
    )
        : base(message) { }
}

public sealed class AppDbContext : DbContext
{
    public AppDbContext(
        DbContextOptions<AppDbContext> options
    )
        : base(options) { }

    public DbSet<User> Users => Set<User>();

    public DbSet<ImportRun> Runs => Set<ImportRun>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var users = modelBuilder.Entity<User>();
        users
            .HasIndex(user => user.PersonnelNumber)
            .IsUnique();

        users
            .HasIndex(user => user.Email)
            .IsUnique()
            .HasDatabaseName("IX_Users_Email");

        users
            .Property(user => user.Email)
            .HasMaxLength(120);

        users
            .Property(user => user.Name)
            .HasMaxLength(80);

        modelBuilder
            .Entity<ImportRun>()
            .Property(run => run.State)
            .HasMaxLength(20);
    }
}

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public sealed record UserDto(
    int PersonnelNumber,
    string Email,
    string Name
);

public sealed record UserRow(
    int PersonnelNumber,
    string Email,
    string Name
);

/// <summary>
/// The user import of an application: a check without target, a strict upsert, an import inside the application's own
/// transaction, and an export of the stored users to a local path.
/// </summary>
public static class EntityFrameworkCoreSample
{
    private const string UsersFile =
        "1,ada@example.org,Ada\n2,grace@example.org,Grace\n3,ada@example.org,Ada Twin\n4,bob@example.org,Bob";

    private const string ValidFile = "1,ada@example.org,Ada\n2,grace@example.org,Grace";
    private static readonly TabularErrorCode s_userInvalid = TabularErrorCode.Create("user.invalid");

    private static readonly TabularErrorCode s_duplicateEmail = TabularErrorCode.Create("user.duplicateEmail");

    private static readonly TabularMap<UserDto> s_importMap = TabularMap
        .For<UserDto>()
        .Column(x => x.PersonnelNumber,
            index: 0,
            key: ColumnKey.Create("personnelNumber"),
            configure: c => c.IncludeSourceValue())
        .Column(x => x.Email, index: 1, key: ColumnKey.Create("email"), configure: c => c.Required())
        .Column(x => x.Name, index: 2, key: ColumnKey.Create("name"))
        .Build();

    private static readonly TabularMap<UserRow> s_exportMap = TabularMap
        .For<UserRow>()
        .Column(x => x.PersonnelNumber,
            index: 0,
            key: ColumnKey.Create("personnelNumber"),
            configure: c => c
                .Header("Personalnummer")
                .HeaderTranslationKey("personnelNumber"))
        .Column(x => x.Email,
            index: 1,
            key: ColumnKey.Create("email"),
            configure: c => c
                .Header("E-Mail")
                .HeaderTranslationKey("email"))
        .Column(x => x.Name,
            index: 2,
            key: ColumnKey.Create("name"),
            configure: c => c
                .Header("Name")
                .HeaderTranslationKey("name"))
        .Build();

    /// <summary>Upsert over the personnel number; the entity keeps its own id on every update.</summary>
    private static TabularWriteTarget<UserDto, User> Upsert(
        TabularDatabase<AppDbContext> database
    ) => database.PrepareUpsert<UserDto, User>(
        set: context => context.Users,
        match: TabularMatch
            .For<UserDto, User>(s_importMap)
            .Part(dto => dto.PersonnelNumber, user => user.PersonnelNumber)
            .UseUniqueKey()
            .Build(),
        create: dto => User.Create(dto.PersonnelNumber, dto.Email, dto.Name),
        update: (user, dto) => user.Change(dto.Email, dto.Name),
        errors: errors => errors
            .Expect<InvalidUserException>(s_userInvalid)
            .Database(exception
                => exception.InnerException?.Message.Contains("IX_Users_Email", StringComparison.Ordinal) is true
                    ? s_duplicateEmail
                    : null));

    public static async Task RunAsync(
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        await using var database =
            await SampleDatabase<AppDbContext>.StartAsync(options => new AppDbContext(options),
                null,
                cancellationToken);

        IDbContextFactory<AppDbContext> factory =
            new SampleContextFactory<AppDbContext>(database.Options, options => new AppDbContext(options));

        var csv = new CsvFormat(new CsvFormatOptions());
        await CheckAsync(csv, output, cancellationToken);
        await UpsertAsync(csv, factory, output, cancellationToken);
        await ImportInTransactionAsync(csv, factory, output, cancellationToken);
        await ExportAsync(csv, factory, output, cancellationToken);
    }

    /// <summary>Validates the file without a write target.</summary>
    private static async Task CheckAsync(
        CsvFormat csv,
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        // 1. The check: an import without target. A repeated personnel number would reject all its occurrences.
        var check = await csv.ImportAsync(Input(UsersFile),
            s_importMap,
            new CsvImportOptions
            {
                Strategy = ImportStrategy.Partial,
                SourceKey = s_importMap.Key(dto => dto.PersonnelNumber),
            },
            cancellationToken);

        await output.WriteLineAsync($"Check: {check.Status}, {check.Items!.Count} valid rows");
    }

    /// <summary>Writes the file strictly and partially into the users table.</summary>
    private static async Task UpsertAsync(
        CsvFormat csv,
        IDbContextFactory<AppDbContext> factory,
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        // 2. Strict upsert: the duplicate email in record 3 is a row error; nothing is stored.
        var strict = await csv.ImportAsync(Input(UsersFile),
            s_importMap,
            Upsert(TabularDatabase.ForFactory(factory)),
            cancellationToken);

        await output.WriteLineAsync(
            $"Strict upsert: {strict.Status}, imported {strict.RowsImported}, errors {string.Join(", ", strict.Errors.Select(error => $"{error.Code} in record {error.Location.RecordNumber}"))}");

        // 3. Partial upsert in commit groups of two: the other rows are stored.
        var partial = await csv.ImportAsync(Input(UsersFile),
            s_importMap,
            Upsert(TabularDatabase.ForFactory(factory))
                .WithOptions(new TabularWriteOptions { CommitGroupSize = 2 }),
            new CsvImportOptions { Strategy = ImportStrategy.Partial },
            cancellationToken);

        await output.WriteLineAsync(
            $"Partial upsert: {partial.Status}, imported {partial.RowsImported}, rejected {partial.RowsRejected}");
    }

    /// <summary>Imports inside a transaction the application owns and commits.</summary>
    private static async Task ImportInTransactionAsync(
        CsvFormat csv,
        IDbContextFactory<AppDbContext> factory,
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        // 4. Inside the application's transaction: Tabular never commits; the rows wait for the application's commit.
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        context.Runs.Add(new ImportRun { State = "started" });
        await context.SaveChangesAsync(cancellationToken);
        var joined = Upsert(TabularDatabase.ForContext(context))
            .WithOptions(new TabularWriteOptions { JoinExistingTransaction = true });

        var report = await csv.ImportAsync(Input(ValidFile), s_importMap, joined, cancellationToken);
        await output.WriteLineAsync($"Joined import: {report.Status}, pending {report.RowsPendingCommit}");
        if (report.Status == ReportStatus.Succeeded)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        else
        {
            // The rollback must run even after cancellation, so it gets its own token.
            await transaction.RollbackAsync(CancellationToken.None);
        }
    }

    /// <summary>Exports the stored users into a file published atomically.</summary>
    private static async Task ExportAsync(
        CsvFormat csv,
        IDbContextFactory<AppDbContext> factory,
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        // 5. Export of the stored users: the query runs asynchronously, the file is published atomically.
        var path = Path.Combine(Path.GetTempPath(), $"users-{Guid.NewGuid():N}.csv");
        try
        {
            await using var context = await factory.CreateDbContextAsync(cancellationToken);
            var query = context
                .Users
                .AsNoTracking()
                .OrderBy(user => user.PersonnelNumber)
                .Select(user => new UserRow(user.PersonnelNumber, user.Email, user.Name));

            var export = await csv.ExportAsync(query,
                s_exportMap,
                path,
                new CsvExportOptions
                {
                    WriteHeader = true,
                    ColumnKeys = [ColumnKey.Create("personnelNumber"), ColumnKey.Create("email")],
                    Translations = new Dictionary<string, string>
                    {
                        ["personnelNumber"] = "Number",
                        ["email"] = "Email",
                    },
                },
                cancellationToken);

            await output.WriteLineAsync($"Export: {export.Status}, {export.RowsWritten} rows, {export.PathOutput}");
            await output.WriteLineAsync(await File.ReadAllTextAsync(path, cancellationToken));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static MemoryStream Input(
        string text
    ) => new(Encoding.UTF8.GetBytes(text));
}
