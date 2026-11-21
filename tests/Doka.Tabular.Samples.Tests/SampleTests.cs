using System.Threading;
using Doka.Tabular.Samples.Controller;
using Doka.Tabular.Samples.EntityFrameworkCore;
using Doka.Tabular.Samples.Journals;
using Doka.Tabular.Samples.MinimalApi;
using Doka.Tabular.Samples.MultiSheet;
using Doka.Tabular.Samples.MultiTarget;
using Doka.Tabular.Samples.NestedMaps;
using Doka.Tabular.Samples.Sftp;

namespace Doka.Tabular.Samples.Tests;

/// <summary>Runs every sample in this process through the same entry point as its console program.</summary>
public sealed class SampleTests
{
    [Fact]
    public async Task MinimalApi_ExportsRejectsStreamsAndImports()
    {
        // Act
        var output = await RunAsync(MinimalApiSample.RunAsync);

        // Assert
        Assert.Contains("Buffered CSV export: 200 text/csv", output, StringComparison.Ordinal);
        Assert.Contains("1;Ada Lovelace", output, StringComparison.Ordinal);
        Assert.Contains("Export with an unknown column: 422", output, StringComparison.Ordinal);
        Assert.Contains("Streamed Excel export: 200", output, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"CompletedWithErrors\"", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Controller_UsesInnerCsvOptionsStreamsAndImportsPartially()
    {
        // Act
        var output = await RunAsync(ControllerSample.RunAsync);

        // Assert
        Assert.Contains("A-03;0", output, StringComparison.Ordinal);
        Assert.Contains("Excel export: 200 application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            output,
            StringComparison.Ordinal);

        Assert.Contains("Streamed CSV export: 200 Artikel;Bestand|A-01;12|A-03;0", output, StringComparison.Ordinal);
        Assert.Contains("Streamed export with an unknown column: 422 {\"status\":\"Failed\"",
            output,
            StringComparison.Ordinal);

        Assert.Contains("\"recordNumber\":2", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sftp_ImportsFromForwardOnlyStreamsAndExportsBufferedAndStreamed()
    {
        // Act
        var output = await RunAsync(SftpSample.RunAsync);

        // Assert
        Assert.Contains("Import CompletedWithErrors: 2 imported, 1 rejected", output, StringComparison.Ordinal);
        Assert.Contains("/out/customers-streamed.csv, /out/customers.csv, /review/customers.csv",
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task NestedMaps_RoundTripInBothFormatsAndLanguages()
    {
        // Act
        var output = await RunAsync(NestedMapsSample.RunAsync);

        // Assert
        Assert.Equal(4, CountOf(output, "reimport Succeeded, equal True"));
        Assert.Contains("Order,Billing City,Billing Postal code,Shipping City", output, StringComparison.Ordinal);
        Assert.Contains("A-2,50667,USD", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EntityFrameworkCore_ChecksUpsertsJoinsAndExports()
    {
        // Act
        var output = await RunAsync(EntityFrameworkCoreSample.RunAsync);

        // Assert
        Assert.Contains("Strict upsert: Failed, imported 0, errors user.duplicateEmail in record 3",
            output,
            StringComparison.Ordinal);

        Assert.Contains("Partial upsert: CompletedWithErrors, imported 3, rejected 1",
            output,
            StringComparison.Ordinal);

        Assert.Contains("Joined import: Succeeded, pending 2", output, StringComparison.Ordinal);
        Assert.Contains("Export: Succeeded, 3 rows, Published", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MultiTarget_PassesGeneratedIdsAndResolvesLookups()
    {
        // Act
        var output = await RunAsync(MultiTargetSample.RunAsync);

        // Assert
        Assert.Contains("imported 2, rejected record 2", output, StringComparison.Ordinal);
        Assert.Contains("tabular.lookup.notFound; Ada lives in Leipzig", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MultiSheet_WritesTheReportAndImportsWithBothTransactionScopes()
    {
        // Act
        var output = await RunAsync(MultiSheetSample.RunAsync);

        // Assert
        Assert.Contains("Annual report: Succeeded, 5 rows", output, StringComparison.Ordinal);
        Assert.Contains("One transaction: Failed; Kunden Failed (0), Bestellungen Failed; stored customers 0, orders 0",
            output,
            StringComparison.Ordinal);

        Assert.Contains(
            "Per sheet: CompletedWithErrors; Kunden Succeeded (2), Bestellungen Failed; stored customers 2, orders 0",
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Journals_SkipDuplicatesResolveStoreDownloadAndCleanUp()
    {
        // Act
        var output = await RunAsync(JournalsSample.RunAsync);

        // Assert
        Assert.Contains("customers-copy.csv: Succeeded, decision Skipped, existing True",
            output,
            StringComparison.Ordinal);

        Assert.Contains(
            "Uncertain import: tabular.persistence.commitUnknown; recover Unresolved; retry before resolve Running; resolve Succeeded; retry Processed Succeeded",
            output,
            StringComparison.Ordinal);

        Assert.Contains("Download now: Kunden.csv with 1,Ada|2,Grace|3,Linus", output, StringComparison.Ordinal);
        Assert.Contains("Download after seven days: not found", output, StringComparison.Ordinal);
        Assert.Contains("Cleanup: 1 expired export deleted, 5 import logs deleted, files left 0",
            output,
            StringComparison.Ordinal);
    }

    private static async Task<string> RunAsync(
        Func<TextWriter, CancellationToken, Task> sample
    )
    {
        await using var output = new StringWriter();
        await sample(output, TestContext.Current.CancellationToken);

        return output.ToString();
    }

    private static int CountOf(
        string text,
        string value
    ) => text.Split(value)
            .Length
        - 1;
}
