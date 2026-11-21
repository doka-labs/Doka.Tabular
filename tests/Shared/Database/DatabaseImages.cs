// WHY: Linked sources compile in projects with different global usings, so they import what they use; in a
// project with matching global usings these directives look redundant, which the cleanup must not act on.
// ReSharper disable RedundantUsingDirective

#pragma warning disable IDE0005

// WHY: The database tests and the database samples link this file; each uses only some of its members.
// ReSharper disable UnusedMember.Global
using System;
using System.IO;
using System.Reflection;

namespace Doka.Tabular.Testing;

/// <summary>
/// Reads the digest-pinned image references from the image manifest that the linking project embeds under the
/// logical name <c>Tabular.TestImages</c>.
/// </summary>
internal static class DatabaseImages
{
    public static string Get(
        string stage
    )
    {
        using var stream = Assembly
                .GetExecutingAssembly()
                .GetManifestResourceStream("Tabular.TestImages")
            ?? throw new InvalidOperationException("The embedded image manifest is missing.");

        using var reader = new StreamReader(stream);

        // ReSharper disable once MethodHasAsyncOverload
        // WHY: Fixture setup reads a tiny embedded resource once before any asynchronous work.
        foreach (var line in reader
            .ReadToEnd()
            .Split('\n'))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts is ["FROM", var image, "AS", var name]
                && name == stage)
            {
                return image;
            }
        }

        throw new InvalidOperationException($"The image manifest has no stage {stage}.");
    }

    /// <summary>Returns the version of an image reference such as <c>mariadb:12.3.3@sha256:...</c>.</summary>
    public static Version VersionOf(
        string image
    ) => Version.Parse(
        image[(image.IndexOf(':', StringComparison.Ordinal) + 1)..image.IndexOf('@', StringComparison.Ordinal)]);
}
