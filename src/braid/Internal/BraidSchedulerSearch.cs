namespace Braid.Internal;

internal static class BraidSchedulerSearch
{
    internal static Exception? FindFirstFailedException(List<BraidTask> tasks)
    {
        for (var index = 0; index < tasks.Count; index++)
        {
            var task = tasks[index];
            if (task.Exception is not null)
            {
                return task.Exception;
            }
        }

        return null;
    }

    internal static BraidTask[] CollectWaitingTasksSortedById(List<BraidTask> tasks)
    {
        var waitingCount = 0;
        for (var index = 0; index < tasks.Count; index++)
        {
            if (tasks[index].State is BraidTaskState.Waiting)
            {
                waitingCount++;
            }
        }

        if (waitingCount is 0)
        {
            return [];
        }

        var waitingTasks = new BraidTask[waitingCount];
        var writeIndex = 0;
        for (var index = 0; index < tasks.Count; index++)
        {
            var task = tasks[index];
            if (task.State is BraidTaskState.Waiting)
            {
                waitingTasks[writeIndex++] = task;
            }
        }

        Array.Sort(waitingTasks, static (left, right) => left.Id.CompareTo(right.Id));
        return waitingTasks;
    }

    internal static BraidProbeWaitDiagnostic[] CollectProbeWaitDiagnostics(List<BraidTask> tasks, BraidTaskState state)
    {
        var matches = new List<BraidTask>();
        for (var index = 0; index < tasks.Count; index++)
        {
            var task = tasks[index];
            if (task.State == state && task.LastProbeName is not null)
            {
                matches.Add(task);
            }
        }

        if (matches.Count is 0)
        {
            return [];
        }

        matches.Sort(static (left, right) => left.Id.CompareTo(right.Id));
        var diagnostics = new BraidProbeWaitDiagnostic[matches.Count];
        for (var matchIndex = 0; matchIndex < matches.Count; matchIndex++)
        {
            var task = matches[matchIndex];
            diagnostics[matchIndex] = new BraidProbeWaitDiagnostic(task.WorkerId, task.LastProbeName!);
        }

        return diagnostics;
    }

    internal static BraidTask? FindWaitingTask(BraidTask[] waitingTasks, string workerId, string probeName)
    {
        for (var index = 0; index < waitingTasks.Length; index++)
        {
            var task = waitingTasks[index];
            if (string.Equals(task.WorkerId, workerId, StringComparison.Ordinal) &&
                string.Equals(task.LastProbeName, probeName, StringComparison.Ordinal))
            {
                return task;
            }
        }

        return null;
    }

    internal static BraidTask? FindHeldTask(List<BraidTask> tasks, string workerId, string probeName)
    {
        for (var index = 0; index < tasks.Count; index++)
        {
            var task = tasks[index];
            if (task.State is BraidTaskState.Held &&
                string.Equals(task.WorkerId, workerId, StringComparison.Ordinal) &&
                string.Equals(task.LastProbeName, probeName, StringComparison.Ordinal))
            {
                return task;
            }
        }

        return null;
    }

    internal static BraidTask? FindSameWorkerBlockedTask(List<BraidTask> tasks, string workerId)
    {
        for (var index = 0; index < tasks.Count; index++)
        {
            var task = tasks[index];
            if (string.Equals(task.WorkerId, workerId, StringComparison.Ordinal) &&
                task.State is BraidTaskState.Waiting or BraidTaskState.Held &&
                task.LastProbeName is not null)
            {
                return task;
            }
        }

        return null;
    }
}
