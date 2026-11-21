using System.Diagnostics.CodeAnalysis;
using static Doka.Tabular.Tests.Infrastructure.TestFiles;

namespace Doka.Tabular.Tests;

/// <summary>Building, reusing, and checking maps; creating DTOs through constructors, setters, and factories.</summary>
public sealed partial class MapConstructionTests
{
    private static readonly TabularMap<MyImportDto> s_userMap = TabularMap
        .For<MyImportDto>()
        .Column(x => x.Id, index: 0)
        .Column(x => x.NumericId, index: 1)
        .Column(x => x.Email, index: 5, key: ColumnKey.Create("email"), configure: c => c.Header("E-Mail"))
        .Column(x => x.FirstName, index: 6)
        .Column(x => x.LastName, index: 8)
        .Column(x => x.DateOfHire, index: 10)
        .Build();

    [Theory]
    [InlineData(TabularType.Csv, false)]
    [InlineData(TabularType.Csv, true)]
    [InlineData(TabularType.Excel, false)]
    [InlineData(TabularType.Excel, true)]
    public async Task BasicForm_MapsTheLineToTheRecord(
        TabularType type,
        bool declaredInAnotherOrder
    )
    {
        // Arrange
        var map = declaredInAnotherOrder
            ? TabularMap
                .For<MyImportDto>()
                .Column(x => x.DateOfHire, index: 10)
                .Column(x => x.LastName, index: 8)
                .Column(x => x.FirstName, index: 6)
                .Column(x => x.Email, index: 5, key: ColumnKey.Create("email"), configure: c => c.Header("E-Mail"))
                .Column(x => x.NumericId, index: 1)
                .Column(x => x.Id, index: 0)
                .Build()
            : s_userMap;

        object?[] line =
        [
            "d93f16aa-3667-4b95-a814-d4275dbd0554", "42", "x", "x", "x", "ada@example.org", "Ada", "x", "Lovelace", "x",
            "2024-04-01",
        ];

        // Act
        var report = await Import.ImportAsync(Create(type, line), type, map, Token);

        // Assert
        Assert.Equal(new MyImportDto(Guid.Parse("d93f16aa-3667-4b95-a814-d4275dbd0554"),
                "ada@example.org",
                "Ada",
                "Lovelace",
                42,
                new DateTime(2024, 4, 1)),
            Assert.Single(report.Items!));
    }

    [Fact]
    public void Build_NegativeIndex_ThrowsWithTheFieldAndWithoutSourceRow()
    {
        // Act
        var exception = Record.Exception(() => TabularMap
            .For<MyImportDto>()
            .Column(x => x.Email, index: -1)
            .Build());

        // Assert
        var error = Assert.Single(Assert
            .IsType<TabularConfigurationException>(exception)
            .Errors);

        Assert.Equal(TabularErrorCodes.Configuration.Invalid, error.Code);
        Assert.Equal("Email", error.Location.PropertyPath?.Value);
        Assert.Null(error.Location.RowNumber);
        Assert.Contains("-1", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_NullArgument_ThrowsArgumentNullException()
    {
        // Act
        var exception = Record.Exception(() => TabularMap
            .For<MyImportDto>()
            .Column<string>(null!, index: 0));

        // Assert
        Assert.IsType<ArgumentNullException>(exception);
    }

    [Fact]
    public void Build_LaterBuilderChanges_DoNotChangeTheBuiltMap()
    {
        // Arrange
        var builder = TabularMap
            .For<OptionalContact>()
            .Column(x => x.Id, index: 0)
            .Column(x => x.Email, index: 1);

        var map = builder.Build();

        // Act
        builder.Column(x => x.IsActive, index: 2);

        // Assert
        Assert.Equal(2, map.Model.Members.Count);
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task SameMap_InConcurrentExports_GivesTheSameResultAsSingleRuns(
        TabularType type
    )
    {
        // Arrange
        var rows = Enumerable
            .Range(1, 500)
            .Select(number => new MyImportDto(Guid.NewGuid(),
                $"u{number}@example.org",
                "F",
                "L",
                number,
                new DateTime(2024, 1, 1)))
            .ToList();

        var first = new TabularExportOptions
        {
            ColumnKeys = [ColumnKey.Create("email"), ColumnKey.Create("NumericId")],
            WriteHeader = true,
        };

        var second = new TabularExportOptions
        {
            ColumnKeys = [ColumnKey.Create("LastName")],
            WriteHeader = true,
            Translations = new Dictionary<string, string>(),
        };

        // Act
        // WHY: The equality with single runs is the checked contract, so the single and the concurrent runs form one
        // action.
        var singleFirst = await Export.ExportAsync(rows, s_userMap, type, first, Token);
        var singleSecond = await Export.ExportAsync(rows, s_userMap, type, second, Token);
        var parallel = await Task.WhenAll(Export.ExportAsync(rows, s_userMap, type, first, Token),
            Export.ExportAsync(rows, s_userMap, type, second, Token));

        // Assert
        Assert.Equal(Cells(type, singleFirst.File!), Cells(type, parallel[0].File!));
        Assert.Equal(Cells(type, singleSecond.File!), Cells(type, parallel[1].File!));
    }

    private static TabularMap<UserWithAddress> UserMap(
        TabularMap<AddressDto> addressMap
    ) => TabularMap
        .For<UserWithAddress>()
        .Column(x => x.Name, index: 0)
        .Nested(x => x.Address, addressMap)
        .Build();

    public sealed record MyImportDto(
        Guid Id,
        string Email,
        string FirstName,
        string LastName,
        int NumericId,
        DateTime DateOfHire
    );

    public sealed record TwoConstructors
    {
        public TwoConstructors(
            Guid id,
            string email
        )
        {
            Id = id;
            Email = email;
        }

        [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
        public TwoConstructors(
            string email,
            Guid id
        )
            : this(id, email) { }

        public Guid Id { get; }

        public string Email { get; }
    }

    public sealed class LongId
    {
        public LongId(
            long numericId
        )
        {
            NumericId = (int)numericId;
        }

        public int NumericId { get; }
    }

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public sealed class ContactDto
    {
        public ContactDto(
            Guid id
        )
        {
            Id = id;
        }

        public Guid Id { get; }

        public string Email { get; init; } = string.Empty;
    }

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public sealed class SetterContact
    {
        public SetterContact(
            Guid id
        )
        {
            Id = id;
        }

        public static int SetterCalls { get; set; }

        public Guid Id { get; }

        // WHY: The test counts calls of a public setter, so the property keeps set instead of init.
        // ReSharper disable once PropertyCanBeMadeInitOnly.Global
        public string Email
        {
            get;
            set
            {
                SetterCalls++;
                field = value;
            }
        } = string.Empty;
    }

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public sealed record OptionalContact(
        Guid Id,
        string Email,
        bool IsActive = true
    );

    [SuppressMessage("Naming",
        "CA1708:Identifiers should differ by more than case",
        Justification = "The binding case needs two properties that differ only by case.")]
    public sealed record AmbiguousCandidates(
        string Email,
        int EMail
    );

    public sealed record AddressDto(string City)
    {
        public static AddressDto Create(
            string city
        ) => city == "invalid"
            ? throw new InvalidAddressException("bad city invalid")
            : new AddressDto(city);
    }

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public sealed record UserWithAddress(
        string Name,
        AddressDto Address
    );

    public sealed class InvalidAddressException : Exception
    {
        public InvalidAddressException(
            string message
        )
            : base(message) { }
    }
}
