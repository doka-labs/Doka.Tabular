using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Doka.Tabular.EntityFrameworkCore;

/// <summary>Registration of the journals in the model of the application; the tables come from its migrations.</summary>

// WHY: CA1708 reads the extension blocks as members differing only by case
// (https://github.com/dotnet/sdk/issues/51716); the fix (https://github.com/dotnet/sdk/pull/51838) is not in
// SDK 10.0.404. Remove the suppression once the pinned SDK contains it.
[SuppressMessage(
    "Naming",
    "CA1708:Identifiers should differ by more than case",
    Justification = "Analyzer defect for extension blocks.")]
[PublicAPI]
public static class TabularJournalModelExtensions
{
    /// <param name="options">The context options.</param>
    extension(
        DbContextOptionsBuilder options
    )
    {
        /// <summary>Registers the standard journal through the context options, without code in <c>OnModelCreating</c>.</summary>
        /// <param name="configure">Changes prefix, directions, and scope comparison.</param>
        /// <returns>The context options.</returns>
        /// <exception cref="ArgumentException">Both directions are disabled or the prefix is invalid.</exception>
        public DbContextOptionsBuilder UseTabularJournal(
            Action<TabularJournalSettings>? configure = null
        )
        {
            ArgumentNullException.ThrowIfNull(options);
            var settings = new TabularJournalSettings();
            configure?.Invoke(settings);
            if (settings is { ImportEnabled: false, ExportEnabled: false })
            {
                throw new ArgumentException(
                    "At least one direction of the journal must be enabled.",
                    nameof(configure));
            }

            JournalModel.CheckPrefix(settings.TablePrefix);

            // WHY: EF replaces an extension of the same type, so a second registration would silently win.
            if (options.Options.FindExtension<TabularJournalOptionsExtension>() is { } existing
                && (existing.Prefix != settings.TablePrefix
                    || existing.Import != settings.ImportEnabled
                    || existing.Export != settings.ExportEnabled
                    || existing.IgnoreScopeCase != settings.IgnoreScopeCase))
            {
                throw new InvalidOperationException(
                    "The standard journal is already registered on these options with other settings.");
            }

            ((IDbContextOptionsBuilderInfrastructure)options).AddOrUpdateExtension(
                new TabularJournalOptionsExtension(
                    settings.TablePrefix,
                    settings.ImportEnabled,
                    settings.ExportEnabled,
                    settings.IgnoreScopeCase));

            return options;
        }
    }

    /// <typeparam name="TContext">The context type.</typeparam>
    /// <param name="options">The context options.</param>
    extension<TContext>(
        DbContextOptionsBuilder<TContext> options
    )
        where TContext : DbContext
    {
        /// <summary>Registers the standard journal through the typed context options, without code in <c>OnModelCreating</c>.</summary>
        /// <param name="configure">Changes prefix, directions, and scope comparison.</param>
        /// <returns>The context options.</returns>
        /// <exception cref="ArgumentException">Both directions are disabled or the prefix is invalid.</exception>
        public DbContextOptionsBuilder<TContext> UseTabularJournal(
            Action<TabularJournalSettings>? configure = null
        )
        {
            ((DbContextOptionsBuilder)options).UseTabularJournal(configure);

            return options;
        }
    }

    /// <param name="modelBuilder">The model builder.</param>
    extension(
        ModelBuilder modelBuilder
    )
    {
        /// <summary>Registers a named import journal in the model.</summary>
        /// <param name="name">The journal name.</param>
        /// <param name="configure">Changes prefix and scope comparison.</param>
        /// <returns>The model builder.</returns>
        /// <exception cref="InvalidOperationException">The journal is already registered with other settings.</exception>
        public ModelBuilder AddTabularImportJournal(
            JournalName name,
            Action<TabularJournalTables>? configure = null
        )
        {
            ArgumentNullException.ThrowIfNull(modelBuilder);
            ArgumentNullException.ThrowIfNull(name);
            var tables = new TabularJournalTables();
            configure?.Invoke(tables);
            JournalModel.CheckPrefix(tables.TablePrefix);
            JournalModel.AddImport(modelBuilder, name, tables.TablePrefix, tables.IgnoreScopeCase);

            return modelBuilder;
        }

        /// <summary>Registers a named export journal in the model.</summary>
        /// <param name="name">The journal name.</param>
        /// <param name="configure">Changes the prefix.</param>
        /// <returns>The model builder.</returns>
        /// <exception cref="InvalidOperationException">The journal is already registered with other settings.</exception>
        public ModelBuilder AddTabularExportJournal(
            JournalName name,
            Action<TabularJournalTables>? configure = null
        )
        {
            ArgumentNullException.ThrowIfNull(modelBuilder);
            ArgumentNullException.ThrowIfNull(name);
            var tables = new TabularJournalTables();
            configure?.Invoke(tables);
            JournalModel.CheckPrefix(tables.TablePrefix);
            JournalModel.AddExport(modelBuilder, name, tables.TablePrefix);

            return modelBuilder;
        }
    }
}
