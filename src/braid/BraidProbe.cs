using Braid.Internal;

namespace Braid;

/// <summary>
/// Provides explicit scheduling points for braid-controlled tests.
/// </summary>
public static class BraidProbe
{
    /// <summary>
    /// Hits a named scheduling point. Outside a braid run this method is a no-op.
    /// </summary>
    /// <param name="name">The probe name.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="ValueTask" /> that completes when the scheduler releases the current operation.</returns>
    public static ValueTask HitAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var scheduler = BraidRunScope.CurrentScheduler;
        var task = BraidRunScope.CurrentTask;

        return scheduler is null || task is null ? ValueTask.CompletedTask : scheduler.HitAsync(task, name, cancellationToken);
    }
}
