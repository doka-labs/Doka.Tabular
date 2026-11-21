namespace Doka.Tabular.EntityFrameworkCore.Specification;

public sealed class User
{
    private User() { }

    public long Id { get; private init; }

    public string Email { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public int PersonnelNumber { get; private init; }

    public Guid Version { get; private set; }

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

        var user = new User
        {
            PersonnelNumber = personnelNumber,
            Version = Guid.NewGuid(),
        };

        return user
            .SetEmail(email)
            .SetName(name);
    }

    public User SetEmail(
        string email
    )
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new InvalidUserException("The email must not be empty.");
        }

        Email = email;
        Version = Guid.NewGuid();

        return this;
    }

    public User SetName(
        string name
    )
    {
        if (name.Length is 0 or > 40)
        {
            throw new InvalidUserException("The name has 1 to 40 characters.");
        }

        Name = name;

        return this;
    }
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
    )
    {
        if (city.Length is 0 or > 40)
        {
            throw new InvalidAddressException("The city has 1 to 40 characters.");
        }

        return new Address
        {
            UserId = userId,
            City = city,
        };
    }

    public void SetCity(
        string city
    ) => City = city;
}

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

public sealed class ProcessingEntry
{
    public long Id { get; init; }

    public string State { get; set; } = string.Empty;
}

public sealed class InvalidUserException : Exception
{
    public InvalidUserException(
        string message
    )
        : base(message) { }
}

public sealed class InvalidAddressException : Exception
{
    public InvalidAddressException(
        string message
    )
        : base(message) { }
}

public sealed record UserDto(
    int PersonnelNumber,
    string Email,
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

public sealed class TabularTestContext : DbContext
{
    public TabularTestContext(
        DbContextOptions<TabularTestContext> options
    )
        : base(options) { }

    public DbSet<User> Users => Set<User>();

    public DbSet<Address> Addresses => Set<Address>();

    public DbSet<UserAlias> Aliases => Set<UserAlias>();

    public DbSet<ProcessingEntry> Entries => Set<ProcessingEntry>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var users = modelBuilder.Entity<User>();
        users.HasKey(user => user.Id);
        users
            .HasIndex(user => user.PersonnelNumber)
            .IsUnique();

        users
            .HasIndex(user => user.Email)
            .IsUnique()
            .HasDatabaseName("IX_Users_Email");

        users
            .Property(user => user.Email)
            .HasMaxLength(100);

        users
            .Property(user => user.Name)
            .HasMaxLength(100);

        users
            .Property(user => user.Version)
            .IsConcurrencyToken();

        var addresses = modelBuilder.Entity<Address>();
        addresses.HasKey(address => address.Id);
        addresses
            .HasIndex(address => address.UserId)
            .IsUnique();

        addresses
            .Property(address => address.City)
            .HasMaxLength(100);

        var aliases = modelBuilder.Entity<UserAlias>();
        aliases.HasKey(alias => new
        {
            alias.TenantId,
            alias.ExternalId,
        });

        aliases
            .Property(alias => alias.ExternalId)
            .HasMaxLength(50);

        var entries = modelBuilder.Entity<ProcessingEntry>();
        entries.HasKey(entry => entry.Id);
        entries
            .Property(entry => entry.State)
            .HasMaxLength(50);

        modelBuilder.AddTabularImportJournal(JournalName.Create("userImports"),
            journal => journal.TablePrefix = "Users_");

        modelBuilder.AddTabularImportJournal(JournalName.Create("caseless"),
            journal =>
            {
                journal.TablePrefix = "Caseless_";
                journal.IgnoreScopeCase = true;
            });
    }
}

/// <summary>Maps and targets shared by the specification cases.</summary>
public static class TestMaps
{
    public static readonly TabularMap<UserDto> Users = TabularMap
        .For<UserDto>()
        .Column(x => x.PersonnelNumber, index: 0)
        .Column(x => x.Email, index: 1)
        .Column(x => x.Name, index: 2)
        .Build();

    public static readonly TabularErrorCode UserInvalid = TabularErrorCode.Create("user.invalid");

    public static readonly TabularErrorCode DuplicateEmail = TabularErrorCode.Create("user.duplicateEmail");

    public static TabularMatch<UserDto, User> UserMatch { get; } = TabularMatch
        .For<UserDto, User>(Users)
        .Part(dto => dto.PersonnelNumber, entity => entity.PersonnelNumber)
        .UseUniqueKey()
        .Build();

    public static TabularWriteTarget<UserDto, User> Upsert(
        TabularDatabase<TabularTestContext> database,
        bool mapDatabaseErrors = true
    ) => database.PrepareUpsert<UserDto, User>(
        set: context => context.Users,
        match: UserMatch,
        create: dto => User.Create(dto.PersonnelNumber, dto.Email, dto.Name),
        update: (entity, dto) => entity
            .SetEmail(dto.Email)
            .SetName(dto.Name),
        errors: errors =>
        {
            errors.Expect<InvalidUserException>(UserInvalid);
            if (mapDatabaseErrors)
            {
                errors.Database(_ => DuplicateEmail);
            }
        });

    public static Stream Csv(
        params string[] lines
    ) => new MemoryStream(Encoding.UTF8.GetBytes(string.Join("\r\n", lines) + "\r\n"));

    public static IEnumerable<string> Rows(
        int count,
        int start = 1
    ) => Enumerable
        .Range(start, count)
        .Select(number => $"{number},user{number}@example.org,User {number}");
}
