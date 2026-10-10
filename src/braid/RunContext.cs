namespace Braid;

/// <summary>
/// Provides task orchestration APIs for a braid run. Only use members while the active
/// <see cref="Runner" /> run callback is executing.
/// </summary>
public sealed class RunContext
{
    private readonly Scheduler _runScheduler;
    private int _isActive = 1;

    internal RunContext(Scheduler runScheduler)
    {
        _runScheduler = runScheduler;
    }

    /// <summary>Gets the scheduling trace from the completed run, when available.</summary>
    public IReadOnlyList<string> TraceSteps { get; private set; } = [];

    internal Dictionary<string, List<string>> WorkerProbeSequences { get; private set; } = [with(StringComparer.Ordinal)];

    /// <summary>Starts a logical concurrent operation controlled by the braid scheduler.</summary>
    /// <param name="operation">The operation to run.</param>
    public void Fork(Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ThrowIfInactive();
        _runScheduler.Fork(operation);
    }

    /// <summary>Starts a logical concurrent operation with a stable worker id.</summary>
    /// <param name="workerId">The stable worker id used in replay schedules.</param>
    /// <param name="operation">The operation to run.</param>
    public void Fork(string workerId, Func<Task> operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        ArgumentNullException.ThrowIfNull(operation);
        ThrowIfInactive();
        _runScheduler.Fork(workerId, operation);
    }

    /// <summary>Runs all forked operations until they complete or the scheduler detects a failure.</summary>
    /// <remarks>A failed join is final: a later join in the same run throws the same exception.</remarks>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="Task" /> that completes when all forked operations complete.</returns>
    /// <exception cref="InvalidOperationException">The run callback has completed, or the call comes from a forked worker.</exception>
    public Task JoinAsync(CancellationToken cancellationToken)
    {
        ThrowIfInactive();
        ThrowIfCalledFromWorker();
        return _runScheduler.JoinAsync(cancellationToken);
    }

    internal void Complete()
    {
        TraceSteps = _runScheduler.GetTraceSnapshot();
        WorkerProbeSequences = _runScheduler.GetWorkerProbeSequences();
        _ = Interlocked.Exchange(ref _isActive, 0);
    }

    private static void ThrowIfCalledFromWorker()
    {
        if (RunTaskSlot.Current != null)
            throw new InvalidOperationException("JoinAsync cannot be called from a forked worker: the join would wait for the worker itself.");
    }

    private void ThrowIfInactive()
    {
        if (Volatile.Read(ref _isActive) == 0)
            throw new InvalidOperationException("RunContext can only be used during the Runner.RunAsync callback.");
    }
}
