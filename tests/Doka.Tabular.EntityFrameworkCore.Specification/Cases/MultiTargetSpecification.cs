namespace Doka.Tabular.EntityFrameworkCore.Specification;

/// <summary>Several targets per row: write order, generated values between targets, lookups, and the row unit.</summary>
public abstract class MultiTargetSpecification
{
    private static readonly CsvFormat s_csv = new(new CsvFormatOptions());

    private static readonly TabularMap<AddressDto> s_addressMap = TabularMap
        .For<AddressDto>()
        .Column(
            x => x.PersonnelNumber,
            index: 0,
            key: ColumnKey.Create("addressPersonnelNumber"),
            configure: c => c.ImportOnly())
        .Column(x => x.City, index: 3)
        .Build();

    private readonly ITestDatabaseProvider _provider;

    protected MultiTargetSpecification(
        ITestDatabaseProvider provider
    )
    {
        _provider = provider;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(ImportStrategy.Strict)]
    [InlineData(ImportStrategy.Partial)]
    public async Task UserAndAddress_GeneratedUserId_ReachesTheAddressOfTheSameRow(
        ImportStrategy strategy
    )
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        var (rowMap, destination) = UserWithAddress(database);

        // Act
        var report = await s_csv.ImportAsync(
            TestMaps.Csv("1,a@example.org,A,Berlin", "2,b@example.org,B,Hamburg", "3,c@example.org,C,Bonn"),
            rowMap,
            destination,
            new CsvImportOptions { Strategy = strategy },
            Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
        await using var context = await database.ContextAsync();
        var pairs = await context
            .Users
            .Join(
                context.Addresses,
                user => user.Id,
                address => address.UserId,
                (user, address) => new
                {
                    user.PersonnelNumber,
                    address.City,
                })
            .OrderBy(pair => pair.PersonnelNumber)
            .ToListAsync(Token);

        Assert.Equal(["Berlin", "Hamburg", "Bonn"], pairs.Select(pair => pair.City));
    }

    [Fact]
    public async Task UserAndAddress_InvalidAddressInPartialImport_RejectsTheUserOfThatRow()
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        var (rowMap, destination) = UserWithAddress(database);
        var city = new string('x', 41);

        // Act
        var report = await s_csv.ImportAsync(
            TestMaps.Csv("1,a@example.org,A,Berlin", $"2,b@example.org,B,{city}", "3,c@example.org,C,Bonn"),
            rowMap,
            destination,
            new CsvImportOptions { Strategy = ImportStrategy.Partial },
            Token);

        // Assert
        Assert.Equal(ReportStatus.CompletedWithErrors, report.Status);
        Assert.Equal(
            2L,
            Assert.Single(report.Errors)
                .Location.RecordNumber);

        Assert.Equal([1, 3], (await database.UsersAsync()).Select(user => user.PersonnelNumber));
    }

    [Fact]
    public async Task UserAndAddress_InvalidAddressInStrictImport_KeepsTheInitialState()
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        var (rowMap, destination) = UserWithAddress(database);

        // Act
        var report =
            await s_csv.ImportAsync(
                TestMaps.Csv("1,a@example.org,A,Berlin", $"2,b@example.org,B,{new string('x', 41)}"),
                rowMap,
                destination,
                Token);

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
        Assert.Empty(await database.UsersAsync());
    }

    [Fact]
    public async Task Lookup_MissingAlias_IsARowErrorAndTheOtherRowsAreStored()
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        var user = User.Create(1, "a@example.org", "A");
        await database.SeedAsync(user);
        await database.SeedAsync(UserAlias.Create(7, "ext-1", user.Id));
        var aliasMap = TabularMap
            .For<AliasDto>()
            .Column(x => x.TenantId, index: 0)
            .Column(x => x.ExternalId, index: 1)
            .Build();

        var addressMap = TabularMap
            .For<AliasAddressDto>()
            .Column(x => x.TenantId, index: 0, key: ColumnKey.Create("addressTenantId"), configure: c => c.ImportOnly())
            .Column(
                x => x.ExternalId,
                index: 1,
                key: ColumnKey.Create("addressExternalId"),
                configure: c => c.ImportOnly())
            .Column(x => x.City, index: 2)
            .Build();

        var rows = RowMap.Create();
        var aliasRole = rows.Add(aliasMap);
        var addressRole = rows.Add(addressMap);
        var rowMap = rows.Build();
        var db = TabularDatabase.ForFactory(database.Factory);
        var lookup = db.PrepareLookup(
            set: context => context.Aliases,
            match: TabularMatch
                .For<AliasDto, UserAlias>(aliasMap)
                .Part(dto => dto.TenantId, alias => alias.TenantId)
                .Part(dto => dto.ExternalId, alias => alias.ExternalId)
                .Build());

        var userId = lookup.Value(aliasRole, alias => alias.UserId);
        var addresses = db.PrepareInsert<AliasAddressDto, Address>(
            set: context => context.Addresses,
            create: (dto, values) => Address.Create(values.Get(userId), dto.City));

        var destination = TabularDestination
            .For(rowMap)
            .Bind(aliasRole, lookup)
            .Bind(addressRole, addresses)
            .Build();

        // Act
        var report = await s_csv.ImportAsync(
            TestMaps.Csv("7,ext-2,Hamburg", "7,ext-1,Berlin"),
            rowMap,
            destination,
            new CsvImportOptions { Strategy = ImportStrategy.Partial },
            Token);

        // Assert
        Assert.Equal(ReportStatus.CompletedWithErrors, report.Status);
        Assert.Equal(
            TabularErrorCodes.Lookup.NotFound,
            Assert.Single(report.Errors)
                .Code);

        await using var context = await database.ContextAsync();
        var stored = await context.Addresses.SingleAsync(Token);
        Assert.Equal((user.Id, "Berlin"), (stored.UserId, stored.City));
    }

    private static (RowMap Map, TabularRowDestination Destination) UserWithAddress(
        TestDatabase database
    )
    {
        var rows = RowMap.Create();
        var userRole = rows.Add(TestMaps.Users);
        var addressRole = rows.Add(s_addressMap);
        var rowMap = rows.Build();
        var db = TabularDatabase.ForFactory(database.Factory);
        var users = TestMaps.Upsert(db);
        var userId = users.Value(userRole, user => user.Id);
        var addresses = db.PrepareUpsert<AddressDto, Address>(
            set: context => context.Addresses,
            match: TabularMatch
                .For<AddressDto, Address>(s_addressMap)
                .Part(dto => dto.PersonnelNumber, address => address.UserId, valueFrom: userId)
                .UseUniqueKey()
                .Build(),
            create: (dto, values) => Address.Create(values.Get(userId), dto.City),
            update: (address, dto) => address.SetCity(dto.City),
            errors: errors => errors.Expect<InvalidAddressException>(TabularErrorCode.Create("address.invalid")));

        return (rowMap, TabularDestination
            .For(rowMap)
            .Bind(userRole, users)
            .Bind(addressRole, addresses)
            .Build());
    }
}
