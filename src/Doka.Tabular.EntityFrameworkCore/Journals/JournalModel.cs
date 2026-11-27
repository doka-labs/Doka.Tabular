using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Doka.Tabular.EntityFrameworkCore;

/// <summary>Entity types of the journals: shared-type entities named per journal, so several journals use the same CLR types.</summary>
internal static class JournalModel
{
    // WHY: The value keeps its 1.0.0-rc.1 spelling, so the EF model of an application does not change with the
    // rename of the public setting.
    public const string IgnoreImportScopeCaseAnnotation = "DokaTabular:IgnoreScopeCase";

    public static string ImportLog(
        JournalName name
    ) => $"DokaTabular.{name.Key}.ImportLog";

    public static string ImportClaim(
        JournalName name
    ) => $"DokaTabular.{name.Key}.ImportClaim";

    public static string ExportLog(
        JournalName name
    ) => $"DokaTabular.{name.Key}.ExportLog";

    public static void CheckPrefix(
        string prefix
    )
    {
        ArgumentNullException.ThrowIfNull(prefix);
        if (prefix.Length > 32
            || !prefix.All(character => char.IsAsciiLetterOrDigit(character) || character == '_'))
        {
            throw new ArgumentException(
                "A table prefix has at most 32 ASCII letters, digits, or underscores.",
                nameof(prefix));
        }
    }

    public static void AddImport(
        ModelBuilder modelBuilder,
        JournalName name,
        string prefix,
        bool ignoreImportScopeCase
    )
    {
        var comparison = ignoreImportScopeCase
            ? "true"
            : "false";

        if (Registered(modelBuilder, ImportLog(name), prefix + "ImportLogs"))
        {
            var claim = modelBuilder.Model.FindEntityType(ImportClaim(name))!;
            if (!Equals(
                claim.FindAnnotation(IgnoreImportScopeCaseAnnotation)
                    ?.Value,
                comparison))
            {
                throw new InvalidOperationException(
                    $"The import journal {name} is already registered with another scope comparison.");
            }

            return;
        }

        modelBuilder.SharedTypeEntity<TabularImportLog>(ImportLog(name), log => ImportLogTable(log, prefix));
        modelBuilder.SharedTypeEntity<TabularImportClaim>(
            ImportClaim(name),
            claim => ImportClaimTable(claim, prefix, comparison));
    }

    private static void ImportLogTable(
        EntityTypeBuilder<TabularImportLog> log,
        string prefix
    )
    {
        log.ToTable(prefix + "ImportLogs");
        log.HasKey(entry => entry.Id);
        log
            .Property(entry => entry.Id)
            .ValueGeneratedNever();

        Scope(log.Property(entry => entry.Scope));
        log
            .Property(entry => entry.FileName)
            .HasMaxLength(256);

        Hash(log.Property(entry => entry.Hash));
        Time(log.Property(entry => entry.StartedAt));
        Time(log.Property(entry => entry.CompletedAt));
        Time(log.Property(entry => entry.ImportedAt));
        Time(log.Property(entry => entry.ResolvedAt));
        log
            .Property(entry => entry.ResolutionReason)
            .HasMaxLength(1000);

        log
            .Property<Guid>("Version")
            .IsConcurrencyToken();

        log
            .HasIndex(entry => entry.CompletedAt)
            .HasDatabaseName($"IX_{prefix}ImportLogs_CompletedAt");
    }

    private static void ImportClaimTable(
        EntityTypeBuilder<TabularImportClaim> claim,
        string prefix,
        string ignoreImportScopeCase
    )
    {
        claim.ToTable(prefix + "ImportClaims");

        // WHY: The key is a hex digest of the scope, so its uniqueness does not depend on a case-insensitive
        // database collation such as utf8mb4_unicode_ci; the readable scope stays in its own column.
        claim
            .Property<string>(JournalStore.ScopeKeyProperty)
            .HasMaxLength(64)
            .IsFixedLength();

        claim.HasKey(JournalStore.ScopeKeyProperty, nameof(TabularImportClaim.Hash));
        claim
            .Property(entry => entry.Scope)
            .HasMaxLength(256);

        Hash(claim.Property(entry => entry.Hash));
        Time(claim.Property(entry => entry.UpdatedAt));
        claim
            .Property<Guid>("Version")
            .IsConcurrencyToken();

        claim.HasAnnotation(IgnoreImportScopeCaseAnnotation, ignoreImportScopeCase);
    }

    public static void AddExport(
        ModelBuilder modelBuilder,
        JournalName name,
        string prefix
    )
    {
        if (Registered(modelBuilder, ExportLog(name), prefix + "ExportLogs"))
        {
            return;
        }

        modelBuilder.SharedTypeEntity<TabularExportLog>(ExportLog(name), log => ExportLogTable(log, prefix));
    }

    private static void ExportLogTable(
        EntityTypeBuilder<TabularExportLog> log,
        string prefix
    )
    {
        log.ToTable(prefix + "ExportLogs");
        log.HasKey(entry => entry.Id);
        log
            .Property(entry => entry.Id)
            .ValueGeneratedNever();

        Scope(log.Property(entry => entry.Scope));
        log
            .Property(entry => entry.FileName)
            .HasMaxLength(256);

        Hash(log.Property(entry => entry.Hash));
        Time(log.Property(entry => entry.StartedAt));
        Time(log.Property(entry => entry.CompletedAt));
        Time(log.Property(entry => entry.ReadyAt));
        Time(log.Property(entry => entry.ExpiresAt));
        Time(log.Property(entry => entry.DeletedAt));
        log
            .Property<Guid>("Version")
            .IsConcurrencyToken();

        log
            .HasIndex(entry => new
            {
                entry.Status,
                entry.ExpiresAt,
            })
            .HasDatabaseName($"IX_{prefix}ExportLogs_Status_ExpiresAt");
    }

    /// <summary>Finds the entity type of a journal part, or <see langword="null" /> when it is not registered.</summary>
    public static IEntityType? Find(
        DbContext context,
        string entityName
    ) => context.Model.FindEntityType(entityName);

    private static bool Registered(
        ModelBuilder modelBuilder,
        string entityName,
        string table
    )
    {
        if (modelBuilder.Model.FindEntityType(entityName) is not { } existing)
        {
            return false;
        }

        if (!string.Equals(existing.GetTableName(), table, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The journal entity {entityName} is already registered with the table {existing.GetTableName()}.");
        }

        return true;
    }

    private static void Scope(
        PropertyBuilder<JournalScope> property
    ) => property
        .HasConversion(
            new ValueConverter<JournalScope, string>(scope => scope.Value, value => JournalScope.Create(value)))
        .HasMaxLength(256);

    // WHY: EF never passes null to a converter, so one converter serves required and optional hash columns.
    private static void Hash(
        PropertyBuilder property
    ) => property
        .HasConversion(new ValueConverter<Sha256Hash, string>(hash => hash.Value, value => Sha256Hash.Parse(value)))
        .HasMaxLength(64)
        .IsFixedLength();

    // WHY: SQLite cannot compare or order DateTimeOffset values in queries; UTC DateTime values compare on every provider.
    private static void Time(
        PropertyBuilder<DateTimeOffset> property
    ) => property.HasConversion(
        new ValueConverter<DateTimeOffset, DateTime>(
            value => value.UtcDateTime,
            value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc))));

    private static void Time(
        PropertyBuilder<DateTimeOffset?> property
    ) => property.HasConversion(
        new ValueConverter<DateTimeOffset?, DateTime?>(
            value => value == null
                ? null
                : value.Value.UtcDateTime,
            value => value == null
                ? null
                : new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc))));
}
