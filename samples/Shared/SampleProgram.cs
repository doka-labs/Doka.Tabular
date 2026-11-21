// WHY: Linked sources compile in projects with different global usings, so they import what they use; in a
// project with matching global usings these directives look redundant, which the cleanup must not act on.
// ReSharper disable RedundantUsingDirective

#pragma warning disable IDE0005
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Doka.Tabular.Samples;

/// <summary>Runs a sample scenario on the console; Ctrl+C cancels it through its token.</summary>
internal static class SampleProgram
{
    public static async Task<int> RunAsync(
        Func<TextWriter, CancellationToken, Task> scenario
    )
    {
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, args) =>
        {
            args.Cancel = true;

            // WHY: The handler is removed in finally before the source is disposed at the end of the method.
            // ReSharper disable once AccessToDisposedClosure
            cancellation.Cancel();
        };

        Console.CancelKeyPress += cancel;
        try
        {
            await scenario(Console.Out, cancellation.Token);
        }
        finally
        {
            // WHY: The handler must not reach the source after it is disposed.
            Console.CancelKeyPress -= cancel;
        }

        return 0;
    }
}
