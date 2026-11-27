using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Doka.Tabular.EntityFrameworkCore;

/// <summary>Adds the standard journal to the model after the application's own model configuration.</summary>
internal sealed class JournalModelCustomizer : RelationalModelCustomizer
{
    public JournalModelCustomizer(
        ModelCustomizerDependencies dependencies
    )
        : base(dependencies) { }

    public override void Customize(
        ModelBuilder modelBuilder,
        DbContext context
    )
    {
        base.Customize(modelBuilder, context);
        var extension = context
            .GetService<IDbContextOptions>()
            .FindExtension<TabularJournalOptionsExtension>();

        if (extension is null)
        {
            return;
        }

        if (extension.Import)
        {
            JournalModel.AddImport(
                modelBuilder,
                JournalName.Default,
                extension.Prefix,
                extension.IgnoreImportScopeCase);
        }

        if (extension.Export)
        {
            JournalModel.AddExport(modelBuilder, JournalName.Default, extension.Prefix);
        }
    }
}
