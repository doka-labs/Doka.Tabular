// WHY: The core, the CSV, the Excel, and the EF Core test applications link this file with different global usings,
// so it imports what it uses; in a project with matching global usings these directives look redundant, which the
// cleanup must not act on.
// ReSharper disable RedundantUsingDirective

#pragma warning disable IDE0005

// WHY: Each test application uses only some of these members.
// ReSharper disable UnusedMember.Global

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Doka.Tabular.Excel;

namespace Doka.Tabular.Tests.Infrastructure;

/// <summary>
/// Names the public overloads of a format by their parameter kinds, as the overload tables name their cases, so a
/// test can compare the tables with the actual surface.
/// </summary>
internal static class OverloadSurface
{
    /// <summary>Gets the names of the public overloads without a destination.</summary>
    public static IReadOnlyList<string> Names(
        Type format,
        string method
    ) =>
    [
        .. Overloads(format, method)
            .Where(overload => !HasDestination(overload))
            .Select(Name)
            .Order(),
    ];

    /// <summary>Gets the names of the public import overloads with a destination.</summary>
    public static IReadOnlyList<string> DestinationNames(
        Type format
    ) =>
    [
        .. Overloads(format, "ImportAsync")
            .Where(HasDestination)
            .Select(Name)
            .Order(),
    ];

    private static IEnumerable<MethodInfo> Overloads(
        Type format,
        string method
    ) => format
        .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        .Where(candidate => candidate.Name == method);

    private static bool HasDestination(
        MethodInfo overload
    ) => overload
        .GetParameters()
        .Any(parameter => parameter.ParameterType.IsGenericType
            && parameter.ParameterType.GetGenericTypeDefinition() == typeof(TabularImportDestination<>));

    private static string Name(
        MethodInfo overload
    )
    {
        var parts = overload
            .GetParameters()
            .Select(parameter => Kind(parameter.ParameterType))
            .OfType<string>()
            .ToList();

        // WHY: A buffered export has no target parameter; the tables name its target before the options, like the
        // others.
        if (overload.ReturnType == typeof(Task<BufferedExportResult>))
        {
            var options = parts.IndexOf("options");
            parts.Insert(
                options < 0
                    ? parts.Count
                    : options,
                "Buffer");
        }

        return string.Join(' ', parts);
    }

    private static string? Kind(
        Type type
    )
    {
        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();

            return definition switch
            {
                _ when definition == typeof(IEnumerable<>) => "Enumerable",
                _ when definition == typeof(IAsyncEnumerable<>) => "AsyncEnumerable",
                _ when definition == typeof(IQueryable<>) => "Queryable",
                _ when definition == typeof(TabularMap<>) => "TabularMap",
                _ when definition == typeof(RowExportMap<>) => "RowExportMap",
                _ when definition == typeof(TabularImportDestination<>) => null,
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, "The parameter kind is unknown."),
            };
        }

        return type switch
        {
            _ when type == typeof(CancellationToken) || type == typeof(TabularType) => null,
            _ when type == typeof(Stream) => "Stream",
            _ when type == typeof(string) => "Path",
            _ when type == typeof(RowMap) => "RowMap",
            _ when type == typeof(TabularFileTarget) => "FileStore",
            _ when type == typeof(ExcelWorkbookExport) || type == typeof(ExcelWorkbookImport) => "Workbook",
            _ when type.Name.EndsWith("Options", StringComparison.Ordinal) => "options",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "The parameter kind is unknown."),
        };
    }
}
