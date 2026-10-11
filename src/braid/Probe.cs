namespace Braid;

/// <summary>Provides explicit scheduling points for braid-controlled tests; braid switches between workers only at their starts and at these probes.</summary>
public static class Probe
{
    /// <summary>Hits a named scheduling point. Outside a braid run this method completes immediately.</summary>
    /// <param name="name">The probe name; null, empty, and whitespace-only values are rejected.</param>
    /// <param name="cancellationToken">
    /// A cancellation token. Inside a braid run it does not end the wait at the probe: the worker observes it when the scheduler releases it,
    /// and the probe then throws. Outside a braid run it is ignored.
    /// </param>
    /// <returns>A <see cref="ValueTask" /> that completes when the scheduler releases the current operation.</returns>
    /// <exception cref="ArgumentException"><paramref name="name" /> is null, empty or whitespace.</exception>
    /// <exception cref="OperationCanceledException">
    /// The scheduler released the worker and <paramref name="cancellationToken" /> was canceled by then, or the run has stopped.
    /// </exception>
    public static ValueTask HitAsync(string name, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var scheduler = RunScope.CurrentScheduler;
        var task = RunTaskSlot.Current;

        return scheduler == null || task == null ? ValueTask.CompletedTask : scheduler.HitAsync(task, name, cancellationToken);
    }
}
