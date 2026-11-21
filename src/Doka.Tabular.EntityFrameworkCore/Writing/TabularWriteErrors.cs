namespace Doka.Tabular.EntityFrameworkCore;

/// <summary>
/// Assigns exceptions of a target to data errors of the row. Assigned errors reject the row; everything else ends the
/// run as a technical error.
/// </summary>
[PublicAPI]
public sealed class TabularWriteErrors
{
    private readonly List<(Type Type, TabularErrorCode Code)> _expected = [];

    internal TabularWriteErrors() { }

    internal Func<Exception, TabularErrorCode?>? DatabaseMapping { get; private set; }

    /// <summary>Assigns exceptions of <typeparamref name="TException" /> and derived types thrown by <c>create</c> or <c>update</c>.</summary>
    /// <typeparam name="TException">The exception type.</typeparam>
    /// <param name="code">The code of the row error.</param>
    /// <returns>This instance.</returns>
    public TabularWriteErrors Expect<TException>(
        TabularErrorCode code
    )
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(code);
        _expected.Add((typeof(TException), code));

        return this;
    }

    /// <summary>
    /// Assigns database errors of a row. The function runs synchronously for every failed save of a row and returns a
    /// code, or <see langword="null" /> for a technical error.
    /// </summary>
    /// <param name="map">The assignment.</param>
    /// <returns>This instance.</returns>
    public TabularWriteErrors Database(
        Func<Exception, TabularErrorCode?> map
    )
    {
        ArgumentNullException.ThrowIfNull(map);
        DatabaseMapping = map;

        return this;
    }

    internal TabularErrorCode? Find(
        Exception exception
    ) => _expected.FirstOrDefault(entry => entry.Type.IsInstanceOfType(exception))
        .Code;
}
