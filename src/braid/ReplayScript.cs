namespace Braid;

/// <summary>Holds the scripted steps of a replay run and, for a run of an exploration, the choices the run makes after those steps.</summary>
internal sealed class ReplayScript
{
    private static readonly Comparer<ReplayStep> ByWorkerId = Comparer<ReplayStep>.Create(static (left, right) => string.CompareOrdinal(left.WorkerId, right.WorkerId));
    private readonly List<ReplayStep[]> _choices = [];
    private readonly List<ReplayStep> _steps;

    internal ReplayScript(IReadOnlyList<ReplayStep> steps, bool explores)
    {
        _steps = [.. steps];
        Explores = explores;
        StartsWorkers = explores;
        for (var index = 0; index < steps.Count; index++)
            StartsWorkers |= steps[index].Kind is ReplayStepKind.Start;
    }

    /// <summary>Gets the choices the run made after the scripted steps, in order. Each entry lists the steps the run could take at that point; it took the first.</summary>
    internal IReadOnlyList<ReplayStep[]> Choices => _choices;

    /// <summary>Gets a value indicating whether the run belongs to an exploration: after the last scripted step it chooses the waiting worker itself instead of failing.</summary>
    internal bool Explores { get; }

    /// <summary>
    /// Gets a value indicating whether the script starts workers itself, as a run of an exploration always does. A script without start steps leaves
    /// the start to the scheduler: every worker starts before the first step, in fork order, as every run did before start steps existed.
    /// </summary>
    internal bool StartsWorkers { get; }

    /// <summary>Gets the scripted steps followed by the steps the run chose itself.</summary>
    internal IReadOnlyList<ReplayStep> Steps => _steps;

    /// <summary>
    /// Chooses the next step among the waiting workers and appends it, so the reported schedule reproduces the whole run. Workers that have not
    /// started come first, in fork order, so the first schedule starts every worker before any probe is passed; started workers follow by worker id.
    /// The order depends only on which workers wait, so a run that replays the same steps makes the same choices.
    /// </summary>
    /// <param name="waitingTasks">The waiting workers, in fork order; at least one.</param>
    internal void AppendChoice(RunTask[] waitingTasks)
    {
        var steps = new ReplayStep[waitingTasks.Length];
        var count = 0;
        for (var index = 0; index < waitingTasks.Length; index++)
        {
            if (waitingTasks[index].LastProbeName == null)
                steps[count++] = ReplayStep.Start(waitingTasks[index].WorkerId);
        }

        var started = count;
        for (var index = 0; index < waitingTasks.Length; index++)
        {
            if (waitingTasks[index].LastProbeName is { } probeName)
                steps[count++] = ReplayStep.Hit(waitingTasks[index].WorkerId, probeName);
        }

        Array.Sort(steps, started, count - started, ByWorkerId);
        _choices.Add(steps);
        _steps.Add(steps[0]);
    }
}
