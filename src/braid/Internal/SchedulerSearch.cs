namespace Braid.Internal;

internal static class SchedulerSearch
{
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

        return operation();
    }
}
