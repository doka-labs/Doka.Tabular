using System.Linq;
using Doka.Tabular.Csv;
using Doka.Tabular.Testing;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Doka.Tabular.EntityFrameworkCore.Tests;

/// <summary>A rejected row restores the owned values of an entity the borrowed context tracked before the row.</summary>
public sealed class OwnedRowTests
{
    private static readonly TabularMap<OwnerDto> s_owners = TabularMap
        .For<OwnerDto>()
        .Column(x => x.Number, index: 0)
        .Column(x => x.Note, index: 1)
        .Build();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RejectedUpdate_DoesNotSaveTheChangeOfAnOwnedValue()
    {
        // Arrange
        await using var database = await SqliteFile.OpenAsync(Token);
        var connection = database.Connection;
        var options = new DbContextOptionsBuilder<OwnerContext>()
            .UseSqlite(connection)
            .Options;

        await using (var setup = new OwnerContext(options))
        {
            await setup.Database.EnsureCreatedAsync(Token);
            var first = new Owner(1, "child-old");
            first.Tags.Add(new Tag { Name = "first" });
            var second = new Owner(2, "two");
            second.Tags.Add(new Tag { Name = "second" });
            setup.Owners.AddRange(first, second);
            await setup.SaveChangesAsync(Token);
        }

        await using var context = new OwnerContext(options);
        await context
            .Owners
            .Include(owner => owner.Tags)
            .LoadAsync(Token);

        var map = TabularMap
            .For<OwnerDto>()
            .Column(x => x.Number, index: 0)
            .Column(x => x.Note, index: 1)
            .Build();

        var match = TabularMatch
            .For<OwnerDto, Owner>(map)
            .Part(dto => dto.Number, owner => owner.Number)
            .Build();

        var target = TabularDatabase
            .ForContext(context)
            .PrepareUpsert<OwnerDto, Owner>(
                set: owners => owners.Owners,
                match: match,
                create: dto => new Owner(dto.Number, dto.Note),
                update: (owner, dto) =>
                {
                    owner.Profile.Note = dto.Note;
                    owner.Tags.Add(new Tag { Name = dto.Note });
                    if (dto.Note == "REJECTED")
                    {
                        throw new RejectedOwnerException();
                    }
                },
                errors: errors => errors.Expect<RejectedOwnerException>(TabularErrorCode.Create("owner.rejected")));

        // Act
        var report = await new CsvFormat(new CsvFormatOptions()).ImportAsync(
            TestInput.Utf8("1,REJECTED\n2,saved"),
            map,
            target,
            new CsvImportOptions { Strategy = ImportStrategy.Partial },
            Token);

        // Assert
        await using var check = new OwnerContext(options);
        var stored = await check
            .Owners
            .OrderBy(owner => owner.Number)
            .Select(owner => owner.Profile.Note)
            .ToListAsync(Token);

        var tags = (await check
                .Owners
                .Include(owner => owner.Tags)
                .OrderBy(owner => owner.Number)
                .ToListAsync(Token))
            .Select(owner => string.Join(
                "|",
                owner
                    .Tags
                    .Select(tag => tag.Name)
                    .Order(StringComparer.Ordinal)));

        Assert.Equal((1L, 1L), (report.RowsImported, report.RowsRejected));
        Assert.Equal(["child-old", "saved"], stored);
        Assert.Equal(["first", "saved|second"], tags);
    }

    [Fact]
    public async Task DatabaseErrorOfAnOwnedEntityOnly_BelongsToTheTargetThatOwnsIt()
    {
        // Arrange
        await using var database = await SqliteFile.OpenAsync(Token);
        var options = new DbContextOptionsBuilder<OwnerContext>()
            .UseSqlite(database.Connection)
            .AddInterceptors(new RejectTag("BAD"))
            .Options;

        await using (var setup = new OwnerContext(options))
        {
            await setup.Database.EnsureCreatedAsync(Token);
        }

        await using var context = new OwnerContext(options);
        var ownerMap = TabularMap
            .For<OwnerDto>()
            .Column(x => x.Number, index: 0)
            .Column(x => x.Note, index: 1)
            .Build();

        var noteMap = TabularMap
            .For<NoteDto>()
            .Column(x => x.Text, index: 2)
            .Build();

        var rows = RowMap.Create();
        var ownerRole = rows.Add(ownerMap);
        var noteRole = rows.Add(noteMap);
        var rowMap = rows.Build();
        var data = TabularDatabase.ForContext(context);
        var destination = TabularDestination
            .For(rowMap)
            .Bind(
                ownerRole,
                data.PrepareInsert<OwnerDto, Owner>(
                    set: owners => owners.Owners,
                    create: dto =>
                    {
                        var owner = new Owner(dto.Number, dto.Note);
                        owner.Tags.Add(new Tag { Name = dto.Note });

                        return owner;
                    },
                    errors: errors => errors.Database(_ => TabularErrorCode.Create("owner.rejected"))))
            .Bind(
                noteRole,
                data.PrepareInsert<NoteDto, NoteEntry>(
                    set: notes => notes.Notes,
                    create: dto => new NoteEntry { Text = dto.Text },
                    errors: errors => errors.Database(_ => TabularErrorCode.Create("note.rejected"))))
            .Build();

        // Act
        var report = await new CsvFormat(new CsvFormatOptions()).ImportAsync(
            TestInput.Utf8("1,BAD,n"),
            rowMap,
            destination,
            new CsvImportOptions { Strategy = ImportStrategy.Partial },
            Token);

        // Assert
        Assert.Equal(
            "owner.rejected",
            Assert.Single(report.Errors)
                .Code.Value);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task DatabaseError_BelongsToTheTargetThatWroteTheEntity_NotToOneThatOnlyFoundIt(
        bool lookup,
        bool writerFirst
    )
    {
        // Arrange
        await using var database = await SqliteFile.OpenAsync(Token);
        var options = new DbContextOptionsBuilder<OwnerContext>()
            .UseSqlite(database.Connection)

            // WHY: With the writer first, a later finder would take the owner itself; with the finder first, it would
            // take the owned profile before the writer changes it. Each order names the entity it would mislead.
            .AddInterceptors(new RejectModifiedOwner(nameTheOwner: writerFirst))
            .Options;

        await SeedOwnerAsync(options);
        await using var context = new OwnerContext(options);
        var (rowMap, destination) = WriterAndFinder(context, lookup, writerFirst);

        // Act
        var report = await new CsvFormat(new CsvFormatOptions()).ImportAsync(
            TestInput.Utf8("1,BAD"),
            rowMap,
            destination,
            new CsvImportOptions { Strategy = ImportStrategy.Partial },
            Token);

        // Assert
        Assert.Null(report.FatalError);
        Assert.Equal(
            "owner.rejected",
            Assert.Single(report.Errors)
                .Code.Value);
    }

    /// <summary>
    /// A writing target and a target that only finds the same owner: a skipped insert or a lookup. Only the writer maps
    /// database errors, so an error assigned to the finder would be a technical failure.
    /// </summary>
    private static (RowMap Map, TabularRowDestination Destination) WriterAndFinder(
        OwnerContext context,
        bool lookup,
        bool writerFirst
    )
    {
        var rows = RowMap.Create();
        var first = rows.Add(s_owners);
        var second = rows.Add(s_owners, e => e.KeyPrefix(ColumnKey.Create("finder")));
        var rowMap = rows.Build();
        var data = TabularDatabase.ForContext(context);
        var match = TabularMatch
            .For<OwnerDto, Owner>(s_owners)
            .Part(dto => dto.Number, owner => owner.Number)
            .Build();

        var writer = data.PrepareUpsert<OwnerDto, Owner>(
            set: owners => owners.Owners,
            match: match,
            create: dto => new Owner(dto.Number, dto.Note),
            update: (owner, dto) => owner.Profile.Note = dto.Note,
            errors: errors => errors.Database(_ => TabularErrorCode.Create("owner.rejected")));

        var builder = TabularDestination.For(rowMap);
        if (lookup)
        {
            var finder = data.PrepareLookup(set: owners => owners.Owners, match: match);
            builder = writerFirst
                ? builder
                    .Bind(first, writer)
                    .Bind(second, finder)
                : builder
                    .Bind(first, finder)
                    .Bind(second, writer);
        }
        else
        {
            var finder = data.PrepareInsert<OwnerDto, Owner>(
                set: owners => owners.Owners,
                match: match,
                create: dto => new Owner(dto.Number, dto.Note));

            builder = writerFirst
                ? builder
                    .Bind(first, writer)
                    .Bind(second, finder)
                : builder
                    .Bind(first, finder)
                    .Bind(second, writer);
        }

        return (rowMap, builder.Build());
    }

    private static async Task SeedOwnerAsync(
        DbContextOptions<OwnerContext> options
    )
    {
        await using var setup = new OwnerContext(options);
        await setup.Database.EnsureCreatedAsync(Token);
        setup.Owners.Add(new Owner(1, "old"));
        await setup.SaveChangesAsync(Token);
    }

    /// <summary>
    /// Rejects the save of a changed profile, naming the owner itself or only its owned profile, as a constraint on
    /// either table would.
    /// </summary>
    private sealed class RejectModifiedOwner : SaveChangesInterceptor
    {
        private readonly bool _nameTheOwner;

        public RejectModifiedOwner(
            bool nameTheOwner
        )
        {
            _nameTheOwner = nameTheOwner;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            var tracker = Assert
                .IsType<OwnerContext>(eventData.Context)
                .ChangeTracker;

            var profiles = tracker
                .Entries<Profile>()
                .Where(entry => entry is { State: EntityState.Modified, Entity.Note: "BAD" })
                .ToList();

            if (profiles.Count == 0)
            {
                return ValueTask.FromResult(result);
            }

            List<EntityEntry> named = _nameTheOwner
                ? [.. tracker.Entries<Owner>()]
                : [.. profiles];

            throw new DbUpdateException("The owner was rejected.", named);
        }
    }

    /// <summary>Rejects the save of an added tag with the given name, naming only that tag, as a failed batch does.</summary>
    private sealed class RejectTag : SaveChangesInterceptor
    {
        private readonly string _name;

        public RejectTag(
            string name
        )
        {
            _name = name;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            var rejected = Assert
                .IsType<OwnerContext>(eventData.Context)
                .ChangeTracker
                .Entries<Tag>()
                .Where(entry => entry.State == EntityState.Added && entry.Entity.Name == _name)
                .ToList();

            if (rejected.Count > 0)
            {
                throw new DbUpdateException("The tag was rejected.", [.. rejected]);
            }

            return ValueTask.FromResult(result);
        }
    }

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    private sealed record NoteDto(
        string Text
    );

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    private sealed class NoteEntry
    {
        public int Id { get; init; }

        public string Text { get; init; } = string.Empty;
    }

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    private sealed record OwnerDto(
        int Number,
        string Note
    );

    private sealed class RejectedOwnerException : Exception;

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    private sealed class Owner
    {
        public Owner(
            int number,
            string note
        )
        {
            Number = number;
            Profile = new Profile { Note = note };
        }

        private Owner() { }

        public int Number { get; private init; }

        public Profile Profile { get; private init; } = new();

        // WHY: A set is no IList; restoring its members needs the collection accessor of EF.
        public HashSet<Tag> Tags { get; private init; } = [];
    }

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    private sealed class Tag
    {
        public string Name { get; set; } = string.Empty;
    }

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    private sealed class Profile
    {
        public string Note { get; set; } = string.Empty;
    }

    private sealed class OwnerContext : DbContext
    {
        public OwnerContext(
            DbContextOptions<OwnerContext> options
        )
            : base(options) { }

        public DbSet<Owner> Owners => Set<Owner>();

        public DbSet<NoteEntry> Notes => Set<NoteEntry>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var owners = modelBuilder.Entity<Owner>();
            owners.HasKey(owner => owner.Number);
            owners
                .Property(owner => owner.Number)
                .ValueGeneratedNever();

            owners.OwnsOne(owner => owner.Profile);

            // WHY: SQLite generates values only for a single-column integer key, not for the default composite key.
            owners.OwnsMany(
                owner => owner.Tags,
                tags =>
                {
                    tags.Property<int>("Id");
                    tags.HasKey("Id");
                });
        }
    }
}
