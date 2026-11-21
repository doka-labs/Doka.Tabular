using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Doka.Tabular.Samples.MinimalApi;

/// <summary>
/// Minimal API: a buffered export decided before the first byte (422 with the report instead of a half file), a
/// streamed export into the response, and an upload import with an explicit format.
/// </summary>
public static class MinimalApiSample
{
    private static WebApplication Build(
        string[] args
    )
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Logging.ClearProviders();
        builder.Services.ConfigureHttpJsonOptions(options
            => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        builder.Services.AddSingleton<ContactStore>();

        // The format instances carry defaults, limits, and logging; the application registers them itself.
        builder.Services.AddSingleton(provider => new CsvFormat(new CsvFormatOptions
        {
            Diagnostics =
                new TabularDiagnosticsOptions { LoggerFactory = provider.GetRequiredService<ILoggerFactory>() },
            Export = new CsvExportOptions { Delimiter = ';' },
        }));

        builder.Services.AddSingleton(provider => new ExcelFormat(new ExcelFormatOptions
        {
            Diagnostics =
                new TabularDiagnosticsOptions { LoggerFactory = provider.GetRequiredService<ILoggerFactory>() },
        }));

        builder.Services.AddSingleton(provider
            => new TabularExport(provider.GetRequiredService<CsvFormat>(), provider.GetRequiredService<ExcelFormat>()));

        builder.Services.AddSingleton(provider
            => new TabularImport(provider.GetRequiredService<CsvFormat>(), provider.GetRequiredService<ExcelFormat>()));

        var app = builder.Build();
        app.MapPost("/contacts/export", ExportBufferedAsync);
        app.MapPost("/contacts/export/stream", ExportStreamingAsync);
        app
            .MapPost("/contacts/import", ImportAsync)
            .DisableAntiforgery();

        return app;
    }

    /// <summary>Starts the application on a free local port, calls every endpoint, and stops it again.</summary>
    public static async Task RunAsync(
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        await using var app = Build([]);
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync(cancellationToken);
        try
        {
            var address = app
                .Services
                .GetRequiredService<IServer>()
                .Features
                .Get<IServerAddressesFeature>()!.Addresses.First();

            using var client = new HttpClient();
            client.BaseAddress = new Uri(address);

            await CallEndpointsAsync(client, output, cancellationToken);
        }
        finally
        {
            await app.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>Calls the buffered and streamed export endpoints and the upload import endpoint.</summary>
    private static async Task CallEndpointsAsync(
        HttpClient client,
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        var valid = await client.PostAsJsonAsync("/contacts/export",
            new ContactExportRequest(TabularType.Csv, ["number", "fullName"], "en"),
            cancellationToken);

        await output.WriteLineAsync(
            $"Buffered CSV export: {(int)valid.StatusCode} {valid.Content.Headers.ContentType}");

        await output.WriteLineAsync(await valid.Content.ReadAsStringAsync(cancellationToken));

        var invalid = await client.PostAsJsonAsync("/contacts/export",
            new ContactExportRequest(TabularType.Excel, ["number", "postCode"], "de"),
            cancellationToken);

        await output.WriteLineAsync($"Export with an unknown column: {(int)invalid.StatusCode}");

        var streamed = await client.PostAsJsonAsync("/contacts/export/stream",
            new ContactExportRequest(TabularType.Excel, ["email", "since"], "de"),
            cancellationToken);

        await output.WriteLineAsync(
            $"Streamed Excel export: {(int)streamed.StatusCode}, {(await streamed.Content.ReadAsByteArrayAsync(cancellationToken)).Length} bytes");

        using var upload = new MultipartFormDataContent();
        upload.Add(new StringContent("Csv"), "type");
        upload.Add(new StringContent("3;carol@example.org;Carol;Shaw;2024-06-01\n0;;Dan;Brown;2024-06-02"),
            "file",
            "contacts.csv");

        var imported = await client.PostAsync("/contacts/import", upload, cancellationToken);
        await output.WriteLineAsync(
            $"Upload import: {(int)imported.StatusCode} {await imported.Content.ReadAsStringAsync(cancellationToken)}");
    }

    private static async Task<IResult> ExportBufferedAsync(
        ContactExportRequest request,
        ContactStore store,
        TabularExport export,
        CancellationToken cancellationToken
    )
    {
        var (report, file) = await export.ExportAsync(store.ReadAsync(cancellationToken),
            ContactCatalog.Map,
            request.Type,
            Options(request),
            cancellationToken);

        // Results.File disposes the stream after sending, which also removes a spilled temporary file.
        return file is null
            ? Results.UnprocessableEntity(ContactCatalog.Response(report))
            : Results.File(file, request.Type.ContentType, $"contacts{request.Type.FileExtension}");
    }

    private static async Task ExportStreamingAsync(
        HttpContext http,
        ContactExportRequest request,
        ContactStore store,
        TabularExport export,
        CancellationToken cancellationToken
    )
    {
        http.Response.ContentType = request.Type.ContentType;
        http.Response.Headers.ContentDisposition = $"attachment; filename=\"contacts{request.Type.FileExtension}\"";

        var report = await export.ExportAsync(store.ReadAsync(cancellationToken),
            ContactCatalog.Map,
            http.Response.Body,
            request.Type,
            Options(request),
            cancellationToken);

        if (report.Status == ReportStatus.Succeeded)
        {
            return;
        }

        if (!http.Response.HasStarted)
        {
            http.Response.ContentType = null;
            http.Response.Headers.ContentDisposition = default;
            await Results
                .UnprocessableEntity(ContactCatalog.Response(report))
                .ExecuteAsync(http);

            return;
        }

        // After the first byte the status code is fixed; a late failure can only abort the download.
        http.Abort();
    }

    private static async Task<IResult> ImportAsync(
        IFormFile file,
        [FromForm] TabularType type,
        ContactStore store,
        TabularImport import,
        CancellationToken cancellationToken
    )
    {
        await using var source = file.OpenReadStream();

        // The client states the format; Tabular checks it against the first bytes and ignores the file name.
        var report = await import.ImportAsync(source,
            type,
            ContactCatalog.Map,
            new TabularImportOptions
            {
                Strategy = ImportStrategy.Partial,
                Csv = new CsvImportOptions { Delimiter = ';' },
            },
            cancellationToken);

        store.AddRange(report.Items ?? []);

        return report.Status is ReportStatus.Succeeded or ReportStatus.CompletedWithErrors
            ? Results.Ok(ContactCatalog.Response(report))
            : Results.UnprocessableEntity(ContactCatalog.Response(report));
    }

    private static TabularExportOptions Options(
        ContactExportRequest request
    ) => new()
    {
        ColumnKeys = [.. request.Columns.Select(ColumnKey.Create)],
        Translations = ContactCatalog.Headers(request.Language),
        WriteHeader = true,
    };
}
