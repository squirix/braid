namespace Braid;

/// <summary>Tracks the fork tasks of a run that are still running and the workers the last shutdown drain abandoned. Callers synchronize access.</summary>
internal sealed class RunningWorkers
{
    private readonly List<Task> _forkTasks = [];
    private string[] _abandonedWorkerIds = [];

    /// <summary>Gets a value indicating whether a fork task has not completed yet.</summary>
    internal bool AnyRunning => _forkTasks.Exists(static task => !task.IsCompleted);

    /// <summary>Starts tracking a fork task.</summary>
    /// <param name="forkTask">The fork task.</param>
    internal void Add(Task forkTask) => _forkTasks.Add(forkTask);

    /// <summary>Appends the workers abandoned by the last shutdown drain to a failure message.</summary>
    /// <param name="message">The failure message.</param>
    /// <returns>The same <paramref name="message" /> instance when no worker was abandoned; otherwise the message followed by the abandoned worker ids.</returns>
    internal string AppendAbandonedWorkers(string message) =>
        _abandonedWorkerIds.Length == 0 ? message
            : $"{message}{Environment.NewLine}Workers still running after the run stopped were abandoned: {string.Join(", ", _abandonedWorkerIds)}. "
            + "Their next probe throws OperationCanceledException; until then they can change shared state.";

    /// <summary>Records the outcome of a shutdown drain.</summary>
    /// <param name="drained">Whether every fork task completed within the drain timeout.</param>
    /// <param name="tasks">The workers of the run; those not completed are recorded as abandoned when the drain timed out.</param>
    internal void RecordDrain(bool drained, List<RunTask> tasks)
    {
        if (drained)
        {
            _abandonedWorkerIds = [];
            return;
        }

        var workerIds = new List<string>();
        for (var index = 0; index < tasks.Count; index++)
        {
            if (tasks[index].State != RunTaskState.Completed)
                workerIds.Add(tasks[index].WorkerId);
        }

        _abandonedWorkerIds = [.. workerIds];
    }

    /// <summary>Stops tracking a fork task.</summary>
    /// <param name="forkTask">The fork task.</param>
    internal void Remove(Task forkTask) => _ = _forkTasks.Remove(forkTask);

    /// <summary>Copies the tracked fork tasks.</summary>
    /// <returns>The tracked fork tasks.</returns>
    internal Task[] Snapshot() => [.. _forkTasks];
}
