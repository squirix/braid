namespace Braid.Internal;

internal static class SchedulerSearch
{
    /// <summary>
    /// Collects tasks whose state matches and that report a probe name.
    /// The caller must hold the scheduler gate so <see cref="RunTask.State" /> reads stay consistent.
    /// </summary>
    /// <param name="tasks">The scheduler task list.</param>
    /// <param name="state">The state to match.</param>
    /// <returns>The matching diagnostics ordered by task id.</returns>
    internal static BraidProbeWaitDiagnostic[] CollectProbeWaitDiagnostics(List<RunTask> tasks, RunTaskState state)
    {
        var matches = new List<RunTask>();
        for (var index = 0; index < tasks.Count; index++)
        {
            var task = tasks[index];
            if (task.State == state && task.LastProbeName != null)
                matches.Add(task);
        }

        if (matches.Count == 0)
            return [];

        matches.Sort(static (left, right) => left.Id.CompareTo(right.Id));
        var diagnostics = new BraidProbeWaitDiagnostic[matches.Count];
        for (var matchIndex = 0; matchIndex < matches.Count; matchIndex++)
        {
            var task = matches[matchIndex];
            diagnostics[matchIndex] = new BraidProbeWaitDiagnostic(task.WorkerId, task.LastProbeName!);
        }

        return diagnostics;
    }

    /// <summary>
    /// Collects tasks in <see cref="RunTaskState.Waiting" /> ordered by <see cref="RunTask.Id" />.
    /// The caller must hold the scheduler gate, because the sizing pass and the fill pass
    /// must observe the same task states (otherwise the fixed-size array can overflow).
    /// </summary>
    /// <param name="tasks">The scheduler task list.</param>
    /// <returns>The waiting tasks ordered by id, or an empty array.</returns>
    internal static RunTask[] CollectWaitingTasksSortedById(List<RunTask> tasks)
    {
        var waitingCount = 0;
        for (var index = 0; index < tasks.Count; index++)
            if (tasks[index].State is RunTaskState.Waiting)
                waitingCount++;

        if (waitingCount == 0)
            return [];

        var waitingTasks = new RunTask[waitingCount];
        var writeIndex = 0;
        for (var index = 0; index < tasks.Count; index++)
        {
            var task = tasks[index];
            if (task.State is RunTaskState.Waiting)
                waitingTasks[writeIndex++] = task;
        }

        Array.Sort(waitingTasks, static (left, right) => left.Id.CompareTo(right.Id));
        return waitingTasks;
    }

    /// <summary>
    /// Returns the first task holding a failure, or null.
    /// The caller must hold the scheduler gate so task state reads stay consistent.
    /// </summary>
    /// <param name="tasks">The scheduler task list.</param>
    /// <returns>The first recorded failure, or null.</returns>
    internal static Exception? FindFirstFailedException(List<RunTask> tasks)
    {
        for (var index = 0; index < tasks.Count; index++)
        {
            var task = tasks[index];
            if (task.Exception != null)
                return task.Exception;
        }

        return null;
    }

    /// <summary>Finds the task held at the given probe, or null. The caller must hold the scheduler gate.</summary>
    /// <param name="tasks">The scheduler task list.</param>
    /// <param name="workerId">The stable worker id.</param>
    /// <param name="probeName">The probe name.</param>
    /// <returns>The held task, or null.</returns>
    internal static RunTask? FindHeldTask(List<RunTask> tasks, string workerId, string probeName)
    {
        for (var index = 0; index < tasks.Count; index++)
        {
            var task = tasks[index];
            var isHeld = task.State is RunTaskState.Held;
            if (isHeld && string.Equals(task.WorkerId, workerId, StringComparison.Ordinal) && string.Equals(task.LastProbeName, probeName, StringComparison.Ordinal))
                return task;
        }

        return null;
    }

    /// <summary>Finds a task blocked at a probe for the worker, or null. The caller must hold the scheduler gate.</summary>
    /// <param name="tasks">The scheduler task list.</param>
    /// <param name="workerId">The stable worker id.</param>
    /// <returns>The blocked task, or null.</returns>
    internal static RunTask? FindSameWorkerBlockedTask(List<RunTask> tasks, string workerId)
    {
        for (var index = 0; index < tasks.Count; index++)
        {
            var task = tasks[index];
            if (string.Equals(task.WorkerId, workerId, StringComparison.Ordinal) && task.State is RunTaskState.Waiting or RunTaskState.Held && task.LastProbeName != null)
                return task;
        }

        return null;
    }

    internal static RunTask? FindWaitingTask(RunTask[] waitingTasks, string workerId, string probeName)
    {
        for (var index = 0; index < waitingTasks.Length; index++)
        {
            var task = waitingTasks[index];
            if (string.Equals(task.WorkerId, workerId, StringComparison.Ordinal) && string.Equals(task.LastProbeName, probeName, StringComparison.Ordinal))
                return task;
        }

        return null;
    }

    internal static Task InvokeForkOperationAsync(object? state)
    {
        if (state is not Func<Task> operation)
            throw new InvalidOperationException("Invalid fork operation.");

        Task? task = operation();
        return task ?? Task.FromException(new InvalidOperationException("Fork operation returned a null task."));
    }
}
