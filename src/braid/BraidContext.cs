using Braid.Internal;

namespace Braid;

/// <summary>
/// Provides task orchestration APIs for a braid run.
/// </summary>
public sealed class BraidContext
{
    private readonly BraidScheduler scheduler;
    private int isActive = 1;

    internal BraidContext(BraidScheduler scheduler)
    {
        this.scheduler = scheduler;
    }

    /// <summary>
    /// Starts a logical concurrent operation controlled by the braid scheduler.
    /// </summary>
    /// <param name="operation">The operation to run.</param>
    public void Fork(Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ThrowIfInactive();
        scheduler.Fork(operation);
    }

    /// <summary>
    /// Runs all forked operations until they complete or the scheduler detects a failure.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="Task" /> that completes when all forked operations complete.</returns>
    public Task JoinAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfInactive();
        return scheduler.JoinAsync(cancellationToken);
    }

    internal void Complete() => _ = Interlocked.Exchange(ref isActive, 0);

    private void ThrowIfInactive()
    {
        if (Volatile.Read(ref isActive) == 0)
        {
            throw new InvalidOperationException("BraidContext can only be used during the Braid.RunAsync callback.");
        }
    }
}
