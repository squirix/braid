namespace Braid;

internal static class SchedulerSearch
{
    /// <summary>
    /// Collects tasks whose state matches and that report a probe name.
    /// The caller must hold the scheduler gate so <see cref="RunTask.State" /> reads stay consistent.
    /// </summary>
    /// <param name="tasks">The scheduler task list.</param>
    /// <param name="state">The state to match.</param>
    /// <returns>The matching diagnostics ordered by task id.</returns>
    internal static ProbeWaitDiagnostic[] CollectProbeWaitDiagnostics(List<RunTask> tasks, RunTaskState state)
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
        var diagnostics = new ProbeWaitDiagnostic[matches.Count];
        for (var matchIndex = 0; matchIndex < matches.Count; matchIndex++)
        {
            var task = matches[matchIndex];
            diagnostics[matchIndex] = new ProbeWaitDiagnostic(task.WorkerId, task.LastProbeName!);
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
        {
            if (tasks[index].State is RunTaskState.Waiting)
                waitingCount++;
        }

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

    /// <summary>Returns the first task holding a failure, or null.</summary>
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

    /// <summary>Finds the task held at the given probe, or null.</summary>
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

    /// <summary>Finds a task blocked at a probe for the worker, or null.</summary>
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

        var task = operation();
        return task ?? Task.FromException(new InvalidOperationException("Fork operation returned a null task."));
    }

    internal static RunTask? TrySelectNextJoinTask(ref SchedulerJoinContext context, CancellationToken cancellationToken, ref bool advancedWithoutRelease)
    {
        var failure = FindFirstFailedException(context.Tasks);
        if (failure != null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw context.CreateException("A forked operation failed.", failure, RunFailureOrigin.UserTest);
        }

        if (context.Tasks.Count == 0 || context.Tasks.TrueForAll(static task => task.State is RunTaskState.Completed))
            return null;

        var waitingTasks = CollectWaitingTasksSortedById(context.Tasks);
        var hasRunningTasks = context.Tasks.Exists(static task => task.State is RunTaskState.Running);
        return SelectNextTask(ref context, waitingTasks, hasRunningTasks, ref advancedWithoutRelease);
    }

    internal static string BuildStepMismatchMessage(int stepIndex, string action, ReplayStep expectedStep, RunTask? sameWorkerBlockedTask)
    {
        var oneBasedIndex = stepIndex + 1;
        return sameWorkerBlockedTask?.LastProbeName == null
            ? $"Scripted schedule step {oneBasedIndex} could not be satisfied: {action} {expectedStep.WorkerId} at {expectedStep.ProbeName}."
            : $"Scripted schedule step {oneBasedIndex} could not be satisfied: {action} {expectedStep.WorkerId} at {expectedStep.ProbeName}; actual probe is {sameWorkerBlockedTask.LastProbeName}.";
    }

    private static RunTask? TrySelectStartupTask(RunTask[] waitingTasks)
    {
        for (var index = 0; index < waitingTasks.Length; index++)
        {
            var task = waitingTasks[index];
            if (task.LastProbeName == null)
                return task;
        }

        return null;
    }

    private static RunTask? SelectArriveStep(ref SchedulerJoinContext context, ReplayStep step, RunTask? waitingTask, RunTask? sameWorkerBlockedTask, bool hasRunningTasks, ref bool advancedWithoutRelease)
    {
        if (waitingTask == null)
        {
            var message = BuildStepMismatchMessage(context.NextScheduleStep, "arrive", step, sameWorkerBlockedTask);
            return hasRunningTasks ? null : throw context.CreateException(message, null, RunFailureOrigin.Scheduler);
        }

        waitingTask.State = RunTaskState.Held;
        context.NextScheduleStep++;
        advancedWithoutRelease = true;
        context.Trace.Add($"{waitingTask.WorkerId} arrival observed at {waitingTask.LastProbeName} (held)");
        return null;
    }

    private static RunTask? SelectHitStep(ref SchedulerJoinContext context, ReplayStep step, RunTask? waitingTask, RunTask? heldTask, RunTask? sameWorkerBlockedTask, bool hasRunningTasks)
    {
        var releasableTask = heldTask ?? waitingTask;
        if (releasableTask == null)
            return hasRunningTasks ? null : throw context.CreateException(BuildStepMismatchMessage(context.NextScheduleStep, "hit", step, sameWorkerBlockedTask), null, RunFailureOrigin.Scheduler);

        context.NextScheduleStep++;
        return releasableTask;
    }

    private static RunTask? SelectNextTask(ref SchedulerJoinContext context, RunTask[] waitingTasks, bool hasRunningTasks, ref bool advancedWithoutRelease)
    {
        var startupTask = TrySelectStartupTask(waitingTasks);
        if (startupTask != null)
            return startupTask;

        if (context.Steps is null)
            return waitingTasks.Length == 0 ? null : waitingTasks[context.Random.NextInt32(waitingTasks.Length)];

        if (context.NextScheduleStep >= context.Steps.Count)
            throw context.CreateException("Scripted schedule was exhausted before all workers completed.", null, RunFailureOrigin.Scheduler);

        return SelectScheduledTask(ref context, waitingTasks, hasRunningTasks, ref advancedWithoutRelease);
    }

    private static RunTask? SelectReleaseStep(ref SchedulerJoinContext context, ReplayStep step, RunTask? heldTask, RunTask? sameWorkerBlockedTask, bool hasRunningTasks)
    {
        if (heldTask == null)
        {
            var message = BuildStepMismatchMessage(context.NextScheduleStep, "release held", step, sameWorkerBlockedTask);
            return hasRunningTasks ? null : throw context.CreateException(message, null, RunFailureOrigin.Scheduler);
        }

        context.NextScheduleStep++;
        return heldTask;
    }

    private static RunTask? SelectScheduledTask(ref SchedulerJoinContext context, RunTask[] waitingTasks, bool hasRunningTasks, ref bool advancedWithoutRelease)
    {
        var step = context.Steps![context.NextScheduleStep];
        var waitingTask = FindWaitingTask(waitingTasks, step.WorkerId, step.ProbeName);
        var heldTask = FindHeldTask(context.Tasks, step.WorkerId, step.ProbeName);
        var sameWorkerBlockedTask = FindSameWorkerBlockedTask(context.Tasks, step.WorkerId);

        return step.Kind switch
        {
            ReplayStepKind.Hit => SelectHitStep(ref context, step, waitingTask, heldTask, sameWorkerBlockedTask, hasRunningTasks),
            ReplayStepKind.Arrive when heldTask != null => throw context.CreateException(
                $"Scripted schedule step {context.NextScheduleStep + 1} could not be satisfied: duplicate Arrive for held {step.WorkerId} at {step.ProbeName}.",
                null,
                RunFailureOrigin.Scheduler),
            ReplayStepKind.Arrive => SelectArriveStep(ref context, step, waitingTask, sameWorkerBlockedTask, hasRunningTasks, ref advancedWithoutRelease),
            ReplayStepKind.Release => SelectReleaseStep(ref context, step, heldTask, sameWorkerBlockedTask, hasRunningTasks),
            _ => throw context.CreateException($"Scripted schedule step {context.NextScheduleStep + 1} has unknown step kind {step.Kind}.", null, RunFailureOrigin.Scheduler),
        };
    }
}
