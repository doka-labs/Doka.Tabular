namespace Doka.Tabular.EntityFrameworkCore;

/// <summary>Entry point for destinations that bind the roles of a row map to targets.</summary>
[PublicAPI]
public static class TabularDestination
{
    /// <summary>Starts the binding of the roles of <paramref name="map" />.</summary>
    /// <param name="map">The row map.</param>
    /// <returns>A new builder.</returns>
    public static TabularDestinationBuilder For(
        RowMap map
    )
    {
        ArgumentNullException.ThrowIfNull(map);

        return new TabularDestinationBuilder(map);
    }
}
