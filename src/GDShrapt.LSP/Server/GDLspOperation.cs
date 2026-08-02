using System;
using System.Threading;
using System.Threading.Tasks;

namespace GDShrapt.LSP;

/// <summary>
/// Runs an LSP request operation with cooperative cancellation and an optional timeout.
/// When <paramref name="timeoutMs"/> is 0 the operation runs inline (no behavior change).
/// On timeout the caller receives the type default (e.g. null) promptly; any still-running
/// work is abandoned and its result discarded.
/// </summary>
internal static class GDLspOperation
{
    public static async Task<T?> RunAsync<T>(Func<CancellationToken, Task<T?>> op, int timeoutMs, CancellationToken ct)
    {
        if (timeoutMs <= 0)
            return await op(ct).ConfigureAwait(false);

        var workTask = Task.Run(() => op(ct), ct);
        var completed = await Task.WhenAny(workTask, Task.Delay(timeoutMs, ct)).ConfigureAwait(false);

        if (completed == workTask)
            return await workTask.ConfigureAwait(false);

        // Either the deadline elapsed or the client cancelled. Surface client cancellation;
        // otherwise return no result so the client is not left waiting on a slow handler.
        ct.ThrowIfCancellationRequested();
        return default;
    }
}
