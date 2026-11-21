// WHY: The EF package runs the shared import and export pipeline, whose plans, cells and sinks are
//      implementation details and not a public extension point.

[assembly: InternalsVisibleTo("Doka.Tabular.EntityFrameworkCore")]

[assembly: InternalsVisibleTo("Doka.Tabular.Tests")]
[assembly: InternalsVisibleTo("Doka.Tabular.Csv.Tests")]
[assembly: InternalsVisibleTo("Doka.Tabular.Excel.Tests")]
[assembly: InternalsVisibleTo("Doka.Tabular.EntityFrameworkCore.Tests")]
[assembly: InternalsVisibleTo("Doka.Tabular.EntityFrameworkCore.Specification")]
[assembly: InternalsVisibleTo("Doka.Tabular.EntityFrameworkCore.MySql.Tests")]
[assembly: InternalsVisibleTo("Doka.Tabular.EntityFrameworkCore.PostgreSql.Tests")]
