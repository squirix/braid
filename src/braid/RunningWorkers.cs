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

    /// <summary>Records that the last shutdown drain abandoned no worker.</summary>
    internal void ClearAbandoned() => _abandonedWorkerIds = [];

    /// <summary>Describes the workers abandoned by the last shutdown drain for a failure message.</summary>
    /// <returns>The description, or <see langword="null" /> when no worker was abandoned.</returns>
    internal string? DescribeAbandoned() =>
        _abandonedWorkerIds.Length == 0 ? null
            : $"Workers still running after the run stopped were abandoned: {string.Join(", ", _abandonedWorkerIds)}. "
            + "Their next probe throws OperationCanceledException; until then they can change shared state.";

    /// <summary>Records the workers that a timed-out shutdown drain abandoned.</summary>
    /// <param name="tasks">The workers of the run; those not completed are recorded as abandoned.</param>
    internal void RecordAbandoned(List<RunTask> tasks)
    {
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
