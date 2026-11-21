namespace Doka.Tabular.Samples.MultiTarget;

public sealed class User
{
    private User() { }

    public long Id { get; private init; }

    public int PersonnelNumber { get; private init; }

    public string Name { get; private set; } = string.Empty;

    public static User Create(
        int personnelNumber,
        string name
    ) => new()
    {
        PersonnelNumber = personnelNumber,
        Name = name,
    };

    public void Rename(
        string name
    ) => Name = name;
}

public sealed class Address
{
    private Address() { }

    public long Id { get; private init; }

    public long UserId { get; private init; }

    public string City { get; private set; } = string.Empty;

    public static Address Create(
        long userId,
        string city
    ) => city.Length is 0 or > 60
        ? throw new InvalidAddressException("The city has 1 to 60 characters.")
        : new Address
        {
            UserId = userId,
            City = city,
        };

    public void Move(
        string city
    ) => City = city;
}

/// <summary>Read-only alias table of the application: tenant and external id to a user.</summary>
public sealed class UserAlias
{
    private UserAlias() { }

    public int TenantId { get; private init; }

    public string ExternalId { get; private init; } = string.Empty;

    public long UserId { get; private init; }

    public static UserAlias Create(
        int tenantId,
        string externalId,
        long userId
    ) => new()
    {
        TenantId = tenantId,
        ExternalId = externalId,
        UserId = userId,
    };
}

public sealed class InvalidAddressException : Exception
{
    public InvalidAddressException(
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

    public DbSet<Address> Addresses => Set<Address>();

    public DbSet<UserAlias> Aliases => Set<UserAlias>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder
            .Entity<User>()
            .HasIndex(user => user.PersonnelNumber)
            .IsUnique();

        modelBuilder
            .Entity<User>()
            .Property(user => user.Name)
            .HasMaxLength(80);

        modelBuilder
            .Entity<Address>()
            .HasIndex(address => address.UserId)
            .IsUnique();

        modelBuilder
            .Entity<Address>()
            .Property(address => address.City)
            .HasMaxLength(60);

        modelBuilder
            .Entity<UserAlias>()
            .HasKey(alias => new
            {
                alias.TenantId,
                alias.ExternalId,
            });

        modelBuilder
            .Entity<UserAlias>()
            .Property(alias => alias.ExternalId)
            .HasMaxLength(40);
    }
}

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public sealed record UserDto(
    int PersonnelNumber,
    string Name
);

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public sealed record AddressDto(
    int PersonnelNumber,
    string City
);

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public sealed record AliasDto(
    int TenantId,
    string ExternalId
);

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public sealed record AliasAddressDto(
    int TenantId,
    string ExternalId,
    string City
);

/// <summary>
/// Several DTOs of one row written to several targets as one unit, with a database-generated user id passed to the
/// address, and a lookup over a two-part key.
/// </summary>
public static class MultiTargetSample
{
    private static readonly TabularMap<UserDto> s_users = TabularMap
        .For<UserDto>()
        .Column(x => x.PersonnelNumber, index: 0, key: ColumnKey.Create("personnelNumber"))
        .Column(x => x.Name, index: 1, key: ColumnKey.Create("name"))
        .Build();

    private static readonly TabularMap<AddressDto> s_addresses = TabularMap
        .For<AddressDto>()
        .Column(x => x.PersonnelNumber,
            index: 0,
            key: ColumnKey.Create("addressPersonnelNumber"),
            configure: c => c.ImportOnly())
        .Column(x => x.City, index: 2, key: ColumnKey.Create("city"))
        .Build();

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
        var db = TabularDatabase.ForFactory(factory);
        await ImportUsersWithAddressesAsync(csv, db, output, cancellationToken);
        await ImportByAliasAsync(csv, db, factory, output, cancellationToken);
    }

    /// <summary>One row writes a user and its address; the address receives the generated user id.</summary>
    private static async Task ImportUsersWithAddressesAsync(
        CsvFormat csv,
        TabularDatabase<AppDbContext> db,
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        // Bind order is write order: the user before its address; the address receives the generated user id.
        var rows = RowMap.Create();
        var userRole = rows.Add(s_users);
        var addressRole = rows.Add(s_addresses);
        var rowMap = rows.Build();
        var users = db.PrepareUpsert<UserDto, User>(
            set: context => context.Users,
            match: TabularMatch
                .For<UserDto, User>(s_users)
                .Part(dto => dto.PersonnelNumber, user => user.PersonnelNumber)
                .UseUniqueKey()
                .Build(),
            create: dto => User.Create(dto.PersonnelNumber, dto.Name),
            update: (user, dto) => user.Rename(dto.Name));

        var userId = users.Value(userRole, user => user.Id);
        var addresses = db.PrepareUpsert<AddressDto, Address>(
            set: context => context.Addresses,
            match: TabularMatch
                .For<AddressDto, Address>(s_addresses)
                .Part(dto => dto.PersonnelNumber, address => address.UserId, valueFrom: userId)
                .UseUniqueKey()
                .Build(),
            create: (dto, values) => Address.Create(values.Get(userId), dto.City),
            update: (address, dto) => address.Move(dto.City),
            errors: errors => errors.Expect<InvalidAddressException>(TabularErrorCode.Create("address.invalid")));

        var destination = TabularDestination
            .For(rowMap)
            .Bind(userRole, users)
            .Bind(addressRole, addresses)
            .Build()
            .WithOptions(new TabularWriteOptions { ReturnItems = true });

        var report = await csv.ImportAsync(Input("1,Ada,Dresden\n2,Grace,\n3,Linus,Bonn"),
            rowMap,
            destination,
            new CsvImportOptions { Strategy = ImportStrategy.Partial },
            cancellationToken);

        await output.WriteLineAsync(
            $"Users with addresses: {report.Status}, imported {report.RowsImported}, rejected record {report.Errors.Single().Location.RecordNumber}");

        foreach (var row in report.Items!)
        {
            await output.WriteLineAsync($"  {row.Get(userRole).Name}: {row.Get(addressRole).City}");
        }
    }

    /// <summary>Lookup: the file names users by tenant and external id; the alias table resolves them.</summary>
    private static async Task ImportByAliasAsync(
        CsvFormat csv,
        TabularDatabase<AppDbContext> db,
        IDbContextFactory<AppDbContext> factory,
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        var adaId = await AddAliasAsync(factory, cancellationToken);
        var aliasMap = TabularMap
            .For<AliasDto>()
            .Column(x => x.TenantId, index: 0)
            .Column(x => x.ExternalId, index: 1)
            .Build();

        var aliasAddressMap = TabularMap
            .For<AliasAddressDto>()
            .Column(x => x.TenantId, index: 0, key: ColumnKey.Create("addressTenantId"), configure: c => c.ImportOnly())
            .Column(x => x.ExternalId,
                index: 1,
                key: ColumnKey.Create("addressExternalId"),
                configure: c => c.ImportOnly())
            .Column(x => x.City, index: 2)
            .Build();

        var lookupRows = RowMap.Create();
        var aliasRole = lookupRows.Add(aliasMap);
        var aliasAddressRole = lookupRows.Add(aliasAddressMap);
        var lookupMap = lookupRows.Build();
        var aliases = db.PrepareLookup(
            set: context => context.Aliases,
            match: TabularMatch
                .For<AliasDto, UserAlias>(aliasMap)
                .Part(dto => dto.TenantId, alias => alias.TenantId)
                .Part(dto => dto.ExternalId, alias => alias.ExternalId, comparer: StringComparer.Ordinal)
                .Build());

        var aliasUserId = aliases.Value(aliasRole, alias => alias.UserId);
        var moved = db.PrepareUpsert<AliasAddressDto, Address>(
            set: context => context.Addresses,
            match: TabularMatch
                .For<AliasAddressDto, Address>(aliasAddressMap)
                .Part(dto => dto.ExternalId, address => address.UserId, valueFrom: aliasUserId)
                .UseUniqueKey()
                .Build(),
            create: (dto, values) => Address.Create(values.Get(aliasUserId), dto.City),
            update: (address, dto) => address.Move(dto.City));

        var lookup = await csv.ImportAsync(Input("7,ext-ada,Leipzig\n7,ext-unknown,Essen"),
            lookupMap,
            TabularDestination
                .For(lookupMap)
                .Bind(aliasRole, aliases)
                .Bind(aliasAddressRole, moved)
                .Build(),
            new CsvImportOptions { Strategy = ImportStrategy.Partial },
            cancellationToken);

        await using var check = await factory.CreateDbContextAsync(cancellationToken);
        await output.WriteLineAsync(
            $"Lookup: {lookup.Status}, {lookup.Errors.Single().Code}; Ada lives in {await check.Addresses.Where(address => address.UserId == adaId).Select(address => address.City).SingleAsync(cancellationToken)}");
    }

    /// <summary>Registers Ada under tenant 7 with the external id ext-ada and returns her user id.</summary>
    private static async Task<long> AddAliasAsync(
        IDbContextFactory<AppDbContext> factory,
        CancellationToken cancellationToken
    )
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var adaId = await context
            .Users
            .Where(user => user.PersonnelNumber == 1)
            .Select(user => user.Id)
            .SingleAsync(cancellationToken);

        context.Aliases.Add(UserAlias.Create(7, "ext-ada", adaId));
        await context.SaveChangesAsync(cancellationToken);

        return adaId;
    }

    private static MemoryStream Input(
        string text
    ) => new(Encoding.UTF8.GetBytes(text));
}
