// WHY: Shared sources compile in test applications with different global usings, so they import what they use; in a
// project with matching global usings these directives look redundant, which the cleanup must not act on.
// ReSharper disable RedundantUsingDirective

#pragma warning disable IDE0005

// WHY: Every test application links these helpers; each application uses only some of them.
// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Global
using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Doka.Tabular.Testing;

/// <summary>
/// A meeting point of concurrent calls without a deadline, so no load can fail it. A participant that ends before it
/// arrived leaves the meeting; the others then go on, and <see cref="Met" /> shows the missing overlap instead of a
/// test that waits forever.
/// </summary>
internal sealed class Meeting : IDisposable
{
    private readonly AsyncLocal<StrongBox<bool>?> _arrived = new();
    private readonly Barrier _barrier;
    private int _met;

    public Meeting(
        int participants
    )
    {
        _barrier = new Barrier(participants);
    }

    /// <summary>Gets the number of participants that passed the meeting point together with the others.</summary>
    public int Met => Volatile.Read(ref _met);

    public void Dispose() => _barrier.Dispose();

    /// <summary>Runs one participant on the thread pool; it leaves the meeting when it ends without having arrived.</summary>
    public Task<T> JoinAsync<T>(
        Func<Task<T>> call,
        CancellationToken cancellationToken
    ) => Task.Run(
        async () =>
        {
            var arrived = new StrongBox<bool>();
            _arrived.Value = arrived;
            try
            {
                return await call();
            }
            finally
            {
                if (!arrived.Value)
                {
                    _barrier.RemoveParticipant();
                }
            }
        },
        cancellationToken);

    /// <summary>
    /// Waits inside the flow of a participant until every other participant arrived or left; a participant arrives
    /// once.
    /// </summary>
    public void Arrive(
        CancellationToken cancellationToken
    )
    {
        var arrived = _arrived.Value ?? throw new InvalidOperationException("Arrive runs outside of a participant.");
        if (arrived.Value)
        {
            return;
        }

        arrived.Value = true;
        _barrier.SignalAndWait(cancellationToken);
        Interlocked.Increment(ref _met);
    }
}
