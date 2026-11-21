using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Doka.Tabular.EntityFrameworkCore;

/// <summary>Options extension of the standard journal; adds the journal entity types to the model.</summary>
internal sealed class TabularJournalOptionsExtension : IDbContextOptionsExtension
{
    public TabularJournalOptionsExtension(
        string prefix,
        bool import,
        bool export,
        bool ignoreScopeCase
    )
    {
        Prefix = prefix;
        Import = import;
        Export = export;
        IgnoreScopeCase = ignoreScopeCase;
        Info = new ExtensionInfo(this);
    }

    public string Prefix { get; }

    public bool Import { get; }

    public bool Export { get; }

    public bool IgnoreScopeCase { get; }

    public DbContextOptionsExtensionInfo Info { get; }

    // WHY: SQLite, PostgreSQL (Npgsql), and the Doka MySQL provider use the relational model customizer and register no own one.
    public void ApplyServices(
        IServiceCollection services
    ) => services.Replace(ServiceDescriptor.Singleton<IModelCustomizer, JournalModelCustomizer>());

    public void Validate(
        IDbContextOptions options
    ) { }

    private sealed class ExtensionInfo : DbContextOptionsExtensionInfo
    {
        public ExtensionInfo(
            TabularJournalOptionsExtension extension
        )
            : base(extension) { }

        public override bool IsDatabaseProvider => false;

        public override string LogFragment => "using Tabular journal ";

        private new TabularJournalOptionsExtension Extension => (TabularJournalOptionsExtension)base.Extension;

        public override int GetServiceProviderHashCode() => HashCode.Combine(Extension.Prefix,
            Extension.Import,
            Extension.Export,
            Extension.IgnoreScopeCase);

        public override bool ShouldUseSameServiceProvider(
            DbContextOptionsExtensionInfo other
        ) => other is ExtensionInfo info
            && info.Extension.Prefix == Extension.Prefix
            && info.Extension.Import == Extension.Import
            && info.Extension.Export == Extension.Export
            && info.Extension.IgnoreScopeCase == Extension.IgnoreScopeCase;

        public override void PopulateDebugInfo(
            IDictionary<string, string> debugInfo
        ) => debugInfo["DokaTabular:Journal"] = HashCode
            .Combine(Extension.Prefix, Extension.Import, Extension.Export, Extension.IgnoreScopeCase)
            .ToString(CultureInfo.InvariantCulture);
    }
}
