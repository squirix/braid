namespace Braid;

/// <summary>Worker-oriented facade for bounded exploration.</summary>
public sealed class BraidExploreContext
{
    private readonly BraidContext _context;

    internal BraidExploreContext(BraidContext context)
    {
        _context = context;
    }

    /// <summary>Registers a logical worker with a stable id and starts it under the braid scheduler.</summary>
    /// <param name="workerId">The stable worker id used in replay schedules.</param>
    /// <param name="operation">The worker operation.</param>
    /// <returns>A <see cref="Task" /> that completes when the worker is forked.</returns>
    public Task WorkerAsync(string workerId, Func<Task> operation)
    {
        _context.Fork(workerId, operation);
        return Task.CompletedTask;
    }

    /// <summary>Waits for all registered workers to complete.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="Task" /> that completes when all workers complete.</returns>
    public Task JoinAsync(CancellationToken cancellationToken) => _context.JoinAsync(cancellationToken);
}
