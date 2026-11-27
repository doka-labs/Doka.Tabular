using System.Security.Cryptography;
using System.Text;

namespace Doka.Tabular.EntityFrameworkCore;

/// <summary>Shared helpers for journal entities.</summary>
internal static class JournalStore
{
    public const string ScopeKeyProperty = "ScopeKey";

    /// <summary>Returns the claim key of a claim scope: the lowercase hex SHA-256 of its UTF-8 bytes.</summary>
    public static string ScopeKey(
        string claimScope
    ) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(claimScope)));

    /// <summary>Adds a new claim with its key.</summary>
    public static void AddClaim(
        DbContext context,
        JournalName name,
        TabularImportClaim claim
    )
    {
        context
            .Set<TabularImportClaim>(JournalModel.ImportClaim(name))
            .Add(claim);

        Touch(context, claim);
    }

    /// <summary>Queries the claim of a claim scope and hash.</summary>
    public static IQueryable<TabularImportClaim> Claim(
        DbContext context,
        JournalName name,
        string claimScope,
        Sha256Hash hash
    )
    {
        var key = ScopeKey(claimScope);

        return context
            .Set<TabularImportClaim>(JournalModel.ImportClaim(name))
            .Where(claim => EF.Property<string>(claim, ScopeKeyProperty) == key && claim.Hash == hash);
    }

    /// <summary>Sets a new value of the internal concurrency token before an update or insert.</summary>
    public static void Touch(
        DbContext context,
        object entity
    ) => context
        .Entry(entity)
        .Property("Version")
        .CurrentValue = Guid.NewGuid();

    /// <summary>Returns the scope as stored in the claim, upper case when the journal ignores the scope case.</summary>
    public static string ClaimScope(
        DbContext context,
        JournalName name,
        JournalScope scope
    )
    {
        var ignoreCase = JournalModel
                .Find(context, JournalModel.ImportClaim(name))
                ?.FindAnnotation(JournalModel.IgnoreImportScopeCaseAnnotation)
                ?.Value as string
            == "true";

        return ignoreCase
            ? scope.Value.ToUpperInvariant()
            : scope.Value;
    }
}
