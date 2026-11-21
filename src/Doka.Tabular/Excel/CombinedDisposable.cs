namespace Doka.Tabular.Excel;

internal sealed class CombinedDisposable : IAsyncDisposable
{
    private readonly IAsyncDisposable[] _items;

    public CombinedDisposable(
        params IAsyncDisposable[] items
    )
    {
        _items = items;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var item in _items)
        {
            await item
                .DisposeAsync()
                .ConfigureAwait(false);
        }
    }
}
