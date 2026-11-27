using System.Threading;
using Doka.Tabular.Csv;
using Doka.Tabular.EntityFrameworkCore;
using Doka.Tabular.Excel;
using Doka.Tabular.Samples.EntityFrameworkCore;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;

namespace Doka.Tabular.Samples.Tests;

/// <summary>A record of the package README example of Doka.Tabular.</summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public sealed record Article(
    string Number,
    int Stock
);

/// <summary>
/// The usage examples of the package descriptions on nuget.org, line for line. The CI builds this project, so a
/// change of the public API that breaks an example fails the build; the core example also runs as a test.
/// </summary>
public static class PackageReadmeExamples
{
    /// <summary>The usage example of the Doka.Tabular package.</summary>
    public static async Task ImportAndExportAsync(
        Stream source,
        Stream target,
        CancellationToken cancellationToken
    )
    {
        var map = TabularMap
            .For<Article>()
            .Column(x => x.Number, index: 0)
            .Column(x => x.Stock, index: 1, configure: c => c.Validate(stock => stock >= 0))
            .Build();

        var csv = new CsvFormat(new CsvFormatOptions());
        var report = await csv.ImportAsync(
            source,
            map,
            new CsvImportOptions { Strategy = ImportStrategy.Partial },
            cancellationToken);

        foreach (var error in report.Errors)
        {
            Console.WriteLine($"{error.Code} in record {error.Location.RecordNumber}");
        }

        var excel = new ExcelFormat(new ExcelFormatOptions());
        await excel.ExportAsync(report.Items!, map, target, cancellationToken);
    }

    /// <summary>The usage example of the Doka.Tabular.EntityFrameworkCore package.</summary>

    // WHY: Compiling the example holds it to the public API; running it needs a database server, which the
    // EntityFrameworkCore sample already exercises with the same target.
    [UsedImplicitly]
    public static async Task ImportUsersAsync(
        IDbContextFactory<AppDbContext> contextFactory,
        TabularMap<UserDto> map,
        Stream source,
        CancellationToken cancellationToken
    )
    {
        var database = TabularDatabase.ForFactory(contextFactory);

        var users = database.PrepareUpsert<UserDto, User>(
            set: context => context.Users,
            match: TabularMatch
                .For<UserDto, User>(map)
                .Part(dto => dto.PersonnelNumber, user => user.PersonnelNumber)
                .UseUniqueKey()
                .Build(),
            create: dto => User.Create(dto.PersonnelNumber, dto.Email, dto.Name),
            update: (user, dto) => user.Change(dto.Email, dto.Name));

        var csv = new CsvFormat(new CsvFormatOptions());
        var report = await csv.ImportAsync(source, map, users, cancellationToken);
        Console.WriteLine($"{report.Status}: {report.RowsImported} rows imported");
    }
}
