using static Doka.Tabular.Tests.Infrastructure.TestFiles;

namespace Doka.Tabular.Tests;

public sealed partial class MapConstructionTests
{
    private static readonly TabularMap<TwoConstructors> s_twoConstructors = TabularMap
        .For<TwoConstructors>()
        .Column(x => x.Id, index: 0)
        .Column(x => x.Email, index: 1)
        .Build();

    private static readonly TabularMap<OptionalContact> s_optionalMapped = TabularMap
        .For<OptionalContact>()
        .Column(x => x.Id, index: 0)
        .Column(x => x.Email, index: 1)
        .Column(x => x.IsActive, index: 2)
        .Build();

    private static readonly object?[][] s_factoryRows = [["Ada", "Berlin"], ["Bob", "invalid"], ["Cy", "Bonn"]];

    private static readonly TabularMap<AddressDto> s_assignedFactory = TabularMap
        .For<AddressDto>()
        .Column(x => x.City, index: 1, configure: c => c.IncludeSourceValue())
        .UseFactory(fields =>
        {
            var city = fields.Of(x => x.City);

            return values => AddressDto.Create(values.Get(city));
        })
        .Expect<InvalidAddressException>(TabularErrorCode.Create("address.invalid"))
        .Build();

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task SeveralMatchingConstructors_FailBeforeReading(
        TabularType type
    )
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act
        var report = await Import.ImportAsync(Create(type, [id.ToString(), "a@example.org"]),
            type,
            s_twoConstructors,
            Token);

        // Assert
        Assert.Equal(TabularErrorCodes.Configuration.Invalid,
            Assert.Single(report.Errors)
                .Code);

        Assert.Contains("factory", report.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, report.RowsRead);
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task SeveralMatchingConstructors_DoNotMatterForTheExport(
        TabularType type
    )
    {
        // Act
        var (report, file) = await Export.ExportAsync(new[] { new TwoConstructors(Guid.NewGuid(), "a@example.org") },
            s_twoConstructors,
            type,
            Token);

        // Assert
        await using var owned = file;
        Assert.Equal(ReportStatus.Succeeded, report.Status);
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task SeveralMatchingConstructors_AreResolvedByTheFactory(
        TabularType type
    )
    {
        // Arrange
        var id = Guid.NewGuid();
        var factory = TabularMap
            .For<TwoConstructors>()
            .Column(x => x.Id, index: 0)
            .Column(x => x.Email, index: 1)
            .UseFactory(fields =>
            {
                var idField = fields.Of(x => x.Id);
                var email = fields.Of(x => x.Email);

                return values => new TwoConstructors(values.Get(idField), values.Get(email));
            })
            .Build();

        // Act
        var report = await Import.ImportAsync(Create(type, [id.ToString(), "a@example.org"]), type, factory, Token);

        // Assert
        Assert.Equal(id,
            Assert.Single(report.Items!)
                .Id);
    }

    [Fact]
    public async Task ParameterBinding_WithoutADirectlyAssignableParameter_IsAConfigurationError()
    {
        // Arrange
        var map = TabularMap
            .For<LongId>()
            .Column(x => x.NumericId, index: 0)
            .Build();

        // Act
        var report = await CsvInstance.ImportAsync(TestInput.Utf8("42"), map, Token);

        // Assert
        Assert.Equal(TabularErrorCodes.Configuration.Invalid,
            Assert.Single(report.Errors)
                .Code);
    }

    [Fact]
    public async Task ParameterBinding_WithoutADirectlyAssignableParameter_IsResolvedByTheFactory()
    {
        // Arrange
        var factory = TabularMap
            .For<LongId>()
            .Column(x => x.NumericId, index: 0)
            .UseFactory(fields =>
            {
                var numericId = fields.Of(x => x.NumericId);

                return values => new LongId(values.Get(numericId));
            })
            .Build();

        // Act
        var report = await CsvInstance.ImportAsync(TestInput.Utf8("42"), factory, Token);

        // Assert
        Assert.Equal(42,
            Assert.Single(report.Items!)
                .NumericId);
    }

    [Fact]
    public async Task ParameterBinding_UsesCSharpNames()
    {
        // Arrange
        var ambiguous = TabularMap
            .For<AmbiguousCandidates>()
            .Column(x => x.Email, index: 0, configure: c => c.Header("E-Mail-Adresse"))
            .Column(x => x.EMail, index: 1, key: ColumnKey.Create("emailNumber"))
            .Build();

        // Act
        var report = await CsvInstance.ImportAsync(TestInput.Utf8("a@example.org,7"), ambiguous, Token);

        // Assert
        Assert.Equal(new AmbiguousCandidates("a@example.org", 7), Assert.Single(report.Items!));
    }

    [Fact]
    public async Task InitProperties_BindEachMappedProperty()
    {
        // Arrange
        var init = TabularMap
            .For<ContactDto>()
            .Column(x => x.Id, index: 0)
            .Column(x => x.Email, index: 1)
            .Build();

        var id = Guid.NewGuid();

        // Act
        var report = await CsvInstance.ImportAsync(TestInput.Utf8($"{id},a@example.org"), init, Token);

        // Assert
        Assert.Equal((id, "a@example.org"), (report.Items![0].Id, report.Items[0].Email));
    }

    [Fact]
    public async Task Setters_BindEachMappedPropertyExactlyOnce()
    {
        // Arrange
        var setter = TabularMap
            .For<SetterContact>()
            .Column(x => x.Id, index: 0)
            .Column(x => x.Email, index: 1)
            .Build();

        var id = Guid.NewGuid();
        SetterContact.SetterCalls = 0;

        // Act
        var report =
            await CsvInstance.ImportAsync(TestInput.Utf8($"{id},a@example.org\n{id},b@example.org"), setter, Token);

        // Assert
        Assert.Equal(2, report.Items!.Count);
        Assert.Equal(2, SetterContact.SetterCalls);
    }

    [Fact]
    public async Task OptionalParameter_KeepsItsDefaultWhenUnmapped()
    {
        // Arrange
        var unmapped = TabularMap
            .For<OptionalContact>()
            .Column(x => x.Id, index: 0)
            .Column(x => x.Email, index: 1)
            .Build();

        // Act
        var report = await CsvInstance.ImportAsync(TestInput.Utf8($"{Guid.NewGuid()},a@example.org"), unmapped, Token);

        // Assert
        Assert.True(Assert.Single(report.Items!)
            .IsActive);
    }

    [Fact]
    public async Task OptionalParameter_TakesTheMappedValue()
    {
        // Act
        var report = await CsvInstance.ImportAsync(TestInput.Utf8($"{Guid.NewGuid()},a@example.org,false"),
            s_optionalMapped,
            Token);

        // Assert
        Assert.False(Assert.Single(report.Items!)
            .IsActive);
    }

    [Fact]
    public async Task OptionalParameter_WithAnInvalidMappedValue_IsAnErrorOfItsProperty()
    {
        // Act
        var report = await CsvInstance.ImportAsync(TestInput.Utf8($"{Guid.NewGuid()},a@example.org,abc"),
            s_optionalMapped,
            Token);

        // Assert
        Assert.Equal("IsActive",
            Assert.Single(report.Errors)
                .Location.PropertyPath?.Value);
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task AssignedFactoryException_IsARowErrorAtTheNestedPath(
        TabularType type
    )
    {
        // Act
        var report = await Import.ImportAsync(Create(type, s_factoryRows),
            type,
            UserMap(s_assignedFactory),
            new TabularImportOptions { Strategy = ImportStrategy.Partial },
            Token);

        // Assert
        var error = Assert.Single(report.Errors);
        Assert.Equal(("address.invalid", 2L, "Address"),
            (error.Code.Value, error.Location.RecordNumber!.Value, error.Location.PropertyPath!.Value));

        Assert.Null(error.Location.ColumnIndex);
        Assert.DoesNotContain("invalid", error.Message, StringComparison.Ordinal);
        Assert.Equal(["Ada", "Cy"], report.Items!.Select(item => item.Name));
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task AssignedFactoryException_UnderStrict_KeepsNoRow(
        TabularType type
    )
    {
        // Act
        var report = await Import.ImportAsync(Create(type, s_factoryRows), type, UserMap(s_assignedFactory), Token);

        // Assert
        Assert.Empty(report.Items!);
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task UnassignedFactoryException_EndsTheRun(
        TabularType type
    )
    {
        // Arrange
        var unassigned = TabularMap
            .For<AddressDto>()
            .Column(x => x.City, index: 1)
            .UseFactory(fields =>
            {
                var city = fields.Of(x => x.City);

                return values => AddressDto.Create(values.Get(city));
            })
            .Build();

        // Act
        var report = await Import.ImportAsync(Create(type, s_factoryRows),
            type,
            UserMap(unassigned),
            new TabularImportOptions { Strategy = ImportStrategy.Partial },
            Token);

        // Assert
        Assert.Equal(TabularErrorCodes.Callback.Failed, report.FatalError?.Code);
        Assert.Equal(["Ada"], report.Items!.Select(item => item.Name));
        Assert.Equal(2, report.RowsRead);
    }

    [Fact]
    public void Factory_ReadingAnUnmappedProperty_IsAConfigurationErrorOfBuild()
    {
        // Act
        var exception = Record.Exception(() => TabularMap
            .For<TwoConstructors>()
            .Column(x => x.Id, index: 0)
            .UseFactory(fields =>
            {
                var id = fields.Of(x => x.Id);
                var email = fields.Of(x => x.Email);

                return values => new TwoConstructors(values.Get(id), values.Get(email));
            })
            .Build());

        // Assert
        var error = Assert.Single(Assert
            .IsType<TabularConfigurationException>(exception)
            .Errors);

        Assert.Equal(TabularErrorCodes.Configuration.Invalid, error.Code);
        Assert.Equal("Email", error.Location.PropertyPath?.Value);
        Assert.Contains("not mapped", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FactoryReturningNull_IsAnInvalidResultAlsoWithAnExpectedException()
    {
        // Arrange
        var address = TabularMap
            .For<AddressDto>()
            .Column(x => x.City, index: 1)
            .UseFactory(fields =>
            {
                _ = fields.Of(x => x.City);

                return _ => null!;
            })
            .Expect<InvalidOperationException>(TabularErrorCode.Create("address.invalid"))
            .Build();

        object?[][] rows = [["Ada", "Berlin"], ["Bob", "Bonn"]];

        // Act
        var report = await Import.ImportAsync(Create(TabularType.Csv, rows),
            TabularType.Csv,
            UserMap(address),
            new TabularImportOptions { Strategy = ImportStrategy.Partial },
            Token);

        // Assert
        Assert.Equal((TabularErrorCodes.Callback.InvalidResult, TabularErrorKind.Technical, 1L),
            (report.FatalError?.Code, report.FatalError?.Kind, report.RowsRead));

        Assert.Empty(report.Items!);
    }

    [Fact]
    public void Factory_ReturningNoCreateFunction_IsAConfigurationErrorOfBuild()
    {
        // Act
        var exception = Assert.Throws<TabularConfigurationException>(() => TabularMap
            .For<TwoConstructors>()
            .Column(x => x.Id, index: 0)
            .Column(x => x.Email, index: 1)
            .UseFactory(_ => null!)
            .Build());

        // Assert
        Assert.Equal(TabularErrorCodes.Configuration.Invalid,
            Assert.Single(exception.Errors)
                .Code);
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task Factory_ReadingAnExportOnlyProperty_FailsBeforeTheFirstRow(
        TabularType type
    )
    {
        // Arrange
        var map = TabularMap
            .For<TwoConstructors>()
            .Column(x => x.Id, index: 0)
            .Column(x => x.Email, index: 1, configure: c => c.ExportOnly())
            .UseFactory(fields =>
            {
                var id = fields.Of(x => x.Id);
                var email = fields.Of(x => x.Email);

                return values => new TwoConstructors(values.Get(id), values.Get(email));
            })
            .Build();

        // Act
        var report = await Import.ImportAsync(Create(type,
            [
                Guid
                    .NewGuid()
                    .ToString(),
                "a@example.org",
            ]),
            type,
            map,
            Token);

        // Assert
        var error = Assert.Single(report.Errors);
        Assert.Equal(TabularErrorCodes.Configuration.Invalid, error.Code);
        Assert.Equal("Email", error.Location.PropertyPath?.Value);
        Assert.Contains("not imported", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, report.RowsRead);
    }
}
