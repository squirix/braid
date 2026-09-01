namespace Braid;

/// <summary>Describes scheduler state captured when a braid run fails.</summary>
public sealed class SchedulerDiagnostics
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SchedulerDiagnostics" /> class.
    /// </summary>
    /// <param name="hasReplaySchedule">Whether a non-empty typed replay schedule was configured.</param>
    /// <param name="lastMatchedReplayStep">The last replay step that was fully consumed, if any.</param>
    /// <param name="lastMatchedReplayStepOneBased">One-based index of <paramref name="lastMatchedReplayStep" /> in the configured schedule.</param>
    /// <param name="waitingWorkers">Workers blocked at probes while waiting to be scheduled.</param>
    /// <param name="heldWorkers">Workers held after an Arrive replay step.</param>
    /// <param name="unusedReplaySteps">Remaining replay steps not yet consumed, with one-based schedule indices.</param>
    public SchedulerDiagnostics(
        bool hasReplaySchedule,
        ReplayStep? lastMatchedReplayStep,
        int? lastMatchedReplayStepOneBased,
        IReadOnlyList<ProbeWaitDiagnostic> waitingWorkers,
        IReadOnlyList<ProbeWaitDiagnostic> heldWorkers,
        IReadOnlyList<(int OneBasedIndex, ReplayStep Step)> unusedReplaySteps)
    {
        HasReplaySchedule = hasReplaySchedule;
        LastMatchedReplayStep = lastMatchedReplayStep;
        LastMatchedReplayStepOneBased = lastMatchedReplayStepOneBased;
        WaitingWorkers = waitingWorkers;
        HeldWorkers = heldWorkers;
        UnusedReplaySteps = unusedReplaySteps;
    }

    /// <summary>Gets a value indicating whether a non-empty typed replay schedule was configured.</summary>
    public bool HasReplaySchedule { get; }

    /// <summary>Gets workers held after an Arrive replay step matched.</summary>
    public IReadOnlyList<ProbeWaitDiagnostic> HeldWorkers { get; }

    /// <summary>Gets the last replay step that was fully consumed, if any.</summary>
    public ReplayStep? LastMatchedReplayStep { get; }

    /// <summary>
    /// Gets the one-based schedule index of <see cref="LastMatchedReplayStep" />, when present.
    /// </summary>
    public int? LastMatchedReplayStepOneBased { get; }

    /// <summary>Gets remaining replay steps not yet consumed, with one-based schedule indices.</summary>
    public IReadOnlyList<(int OneBasedIndex, ReplayStep Step)> UnusedReplaySteps { get; }

    /// <summary>Gets workers blocked at probes while waiting to be scheduled.</summary>
    public IReadOnlyList<ProbeWaitDiagnostic> WaitingWorkers { get; }
}
