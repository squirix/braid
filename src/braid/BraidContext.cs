using Braid.Internal;

namespace Braid;

/// <summary>
/// Provides task orchestration APIs for a braid run. Only use members while the active
/// <see cref="Braid" /> run callback is executing.
/// </summary>
public sealed class BraidContext
{
    private readonly BraidScheduler _scheduler;
    private int _isActive = 1;

    internal BraidContext(BraidScheduler scheduler)
    {
        _scheduler = scheduler;
    }

    /// <summary>
    /// Starts a logical concurrent operation controlled by the braid scheduler.
    /// </summary>
    /// <param name="operation">The operation to run.</param>
    public void Fork(Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ThrowIfInactive();
        _scheduler.Fork(operation);
    }

    /// <summary>
    /// Runs all forked operations until they complete or the scheduler detects a failure.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="Task" /> that completes when all forked operations complete.</returns>
    public Task JoinAsync(CancellationToken cancellationToken)
    {
        ThrowIfInactive();
        return _scheduler.JoinAsync(cancellationToken);
    }

    internal void Complete() => _ = Interlocked.Exchange(ref _isActive, 0);

    private void ThrowIfInactive()
    {
        if (Volatile.Read(ref _isActive) == 0)
        {
            throw new InvalidOperationException("BraidContext can only be used during the Braid.RunAsync callback.");
        }
    }
}
