using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Doka.Tabular.Samples.Controller;

/// <summary>An article; the stock may be zero but not negative.</summary>
public sealed record Article(
    string Number,
    int Stock
);

/// <summary>Body of the export actions: the format, optional CSV settings, and an optional column selection.</summary>
public sealed record ArticleExportRequest(
    TabularType Type,
    char? CsvDelimiter,
    IReadOnlyList<string>? Columns = null
);

/// <summary>Controller variant: the common entry point with call options that carry an inner CSV part.</summary>
[ApiController]
[Route("articles")]
public sealed class ArticlesController : ControllerBase
{
    private static readonly TabularMap<Article> s_map = TabularMap
        .For<Article>()
        .Column(x => x.Number, index: 0, key: ColumnKey.Create("number"), configure: c => c.Header("Artikel"))
        .Column(x => x.Stock,
            index: 1,
            key: ColumnKey.Create("stock"),
            configure: c => c
                .Header("Bestand")
                .Validate(stock => stock >= 0))
        .Build();

    private static readonly List<Article> s_articles = [new("A-01", 12), new("A-03", 0)];

    private readonly TabularExport _export;
    private readonly TabularImport _import;

    public ArticlesController(
        TabularExport export,
        TabularImport import
    )
    {
        _export = export;
        _import = import;
    }

    [HttpPost("export")]
    public async Task<IActionResult> ExportAsync(
        ArticleExportRequest request,
        CancellationToken cancellationToken
    )
    {
        var (report, file) =
            await _export.ExportAsync(s_articles, s_map, request.Type, Options(request), cancellationToken);

        // File(...) disposes the stream after sending.
        return file is null
            ? UnprocessableEntity(new
            {
                report.Status,
                Errors = report.Errors.Select(error => error.Code.Value),
            })
            : File(file, request.Type.ContentType, $"articles{request.Type.FileExtension}");
    }

    [HttpPost("export/stream")]
    public async Task ExportStreamingAsync(
        ArticleExportRequest request,
        CancellationToken cancellationToken
    )
    {
        Response.ContentType = request.Type.ContentType;
        Response.Headers.ContentDisposition = $"attachment; filename=\"articles{request.Type.FileExtension}\"";

        var report = await _export.ExportAsync(s_articles,
            s_map,
            Response.Body,
            request.Type,
            Options(request),
            cancellationToken);

        if (report.Status == ReportStatus.Succeeded)
        {
            return;
        }

        // After the first byte the status code is fixed; a late failure can only abort the download.
        if (Response.HasStarted)
        {
            HttpContext.Abort();

            return;
        }

        Response.Clear();
        await UnprocessableEntity(new
            {
                report.Status,
                Errors = report.Errors.Select(error => error.Code.Value),
            })
            .ExecuteResultAsync(ControllerContext);
    }

    [HttpPost("import")]
    public async Task<IActionResult> ImportAsync(
        IFormFile file,
        [FromForm] TabularType type,
        CancellationToken cancellationToken
    )
    {
        await using var source = file.OpenReadStream();
        var report = await _import.ImportAsync(source,
            type,
            s_map,
            new TabularImportOptions { Strategy = ImportStrategy.Partial },
            cancellationToken);

        return Ok(new
        {
            report.Status,
            report.Items,
            Errors = report.Errors.Select(error => new
            {
                error.Code.Value,
                error.Location.RecordNumber,
            }),
        });
    }

    /// <summary>Settings of the inner CSV part apply only when the call exports CSV; Excel ignores them.</summary>
    private static TabularExportOptions Options(
        ArticleExportRequest request
    ) => new()
    {
        WriteHeader = true,
        ColumnKeys = request
            .Columns
            ?.Select(ColumnKey.Create)
            .ToArray(),
        Csv = new CsvExportOptions { Delimiter = request.CsvDelimiter },
    };
}

public static class ControllerSample
{
    private static WebApplication Build(
        string[] args
    )
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Logging.ClearProviders();
        builder
            .Services
            .AddControllers()
            .AddApplicationPart(typeof(ArticlesController).Assembly)
            .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        builder.Services.AddSingleton(new CsvFormat(new CsvFormatOptions()));
        builder.Services.AddSingleton(new ExcelFormat(new ExcelFormatOptions()));
        builder.Services.AddSingleton(provider
            => new TabularExport(provider.GetRequiredService<CsvFormat>(), provider.GetRequiredService<ExcelFormat>()));

        builder.Services.AddSingleton(provider
            => new TabularImport(provider.GetRequiredService<CsvFormat>(), provider.GetRequiredService<ExcelFormat>()));

        var app = builder.Build();
        app.MapControllers();

        return app;
    }

    /// <summary>Starts the application on a free local port, calls both actions, and stops it again.</summary>
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

            var csv = await client.PostAsJsonAsync("articles/export",
                new ArticleExportRequest(TabularType.Csv, ';'),
                cancellationToken);

            await output.WriteLineAsync($"CSV export: {(int)csv.StatusCode}");
            await output.WriteLineAsync(await csv.Content.ReadAsStringAsync(cancellationToken));
            var excel = await client.PostAsJsonAsync("articles/export",
                new ArticleExportRequest(TabularType.Excel, ';'),
                cancellationToken);

            await output.WriteLineAsync($"Excel export: {(int)excel.StatusCode} {excel.Content.Headers.ContentType}");

            var streamed = await client.PostAsJsonAsync("articles/export/stream",
                new ArticleExportRequest(TabularType.Csv, ';'),
                cancellationToken);

            await output.WriteLineAsync(
                $"Streamed CSV export: {(int)streamed.StatusCode} {(await streamed.Content.ReadAsStringAsync(cancellationToken)).ReplaceLineEndings("|")}");

            var rejected = await client.PostAsJsonAsync("articles/export/stream",
                new ArticleExportRequest(TabularType.Csv, ';', ["unknown"]),
                cancellationToken);

            await output.WriteLineAsync(
                $"Streamed export with an unknown column: {(int)rejected.StatusCode} {await rejected.Content.ReadAsStringAsync(cancellationToken)}");

            using var upload = new MultipartFormDataContent();
            upload.Add(new StringContent("Csv"), "type");
            upload.Add(new StringContent("A-01,12\nA-02,unknown\nA-03,0"), "file", "articles.csv");

            var imported = await client.PostAsync("articles/import", upload, cancellationToken);
            await output.WriteLineAsync(
                $"Import: {(int)imported.StatusCode} {await imported.Content.ReadAsStringAsync(cancellationToken)}");
        }
        finally
        {
            await app.StopAsync(CancellationToken.None);
        }
    }
}
