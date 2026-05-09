namespace Braid.Internal;

internal sealed class BraidScheduler : IDisposable
{
    private readonly Lock gate = new();
    private readonly int iteration;
    private readonly DeterministicRandom random;
    private readonly IReadOnlyList<BraidStep>? schedule;
    private readonly int seed;
    private readonly CancellationTokenSource shutdownCts = new();
    private readonly SemaphoreSlim stateChanged = new(0);
    private readonly SemaphoreSlim joinMutex = new(1, 1);
    private readonly List<BraidTask> tasks = [];
    private readonly TimeSpan timeout;
    private readonly List<string> trace = [];
    private bool joined;
    private int nextScheduleStep;
    private int nextTaskId;

    public BraidScheduler(int seed, int iteration, TimeSpan timeout, IReadOnlyList<BraidStep>? schedule)
    {
        this.seed = seed;
        this.iteration = iteration;
        this.timeout = timeout;
        this.schedule = schedule;
        random = new DeterministicRandom(seed);
    }

    public BraidRunException CreateException(string message, Exception? innerException)
    {
        IReadOnlyList<string> traceSnapshot;
        IReadOnlyList<BraidStep> scheduleSnapshot;
        string resolvedMessage;

        lock (gate)
        {
            traceSnapshot = [.. trace];
            scheduleSnapshot = schedule?.ToArray() ?? [];
            resolvedMessage = AppendReplayState(message);
        }

        return new BraidRunException(resolvedMessage, seed, iteration, traceSnapshot, scheduleSnapshot, innerException);
    }

    public void Dispose()
    {
        shutdownCts.Dispose();
        stateChanged.Dispose();
        joinMutex.Dispose();

        foreach (var task in tasks)
        {
            task.Dispose();
        }
    }

    public void Fork(Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        BraidTask braidTask;

        lock (gate)
        {
            if (joined)
            {
                throw CreateException("Cannot fork after JoinAsync has started.", null);
            }

            braidTask = new BraidTask(++nextTaskId);
            tasks.Add(braidTask);
            trace.Add($"{braidTask.WorkerId} forked");
        }

        braidTask.RunningTask = Task.Run(async () =>
        {
            BraidRunScope.CurrentTask = braidTask;

            try
            {
                await braidTask.WaitForReleaseAsync(shutdownCts.Token).ConfigureAwait(false);
                var opTask = operation() ?? throw new InvalidOperationException("Fork operation returned a null task.");
                await opTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                braidTask.Exception = ex;
            }
            finally
            {
                BraidRunScope.CurrentTask = null;

                lock (gate)
                {
                    braidTask.State = BraidTaskState.Completed;
                    trace.Add($"{braidTask.WorkerId} completed");
                }

                _ = stateChanged.Release();
            }
        });
    }

    public async ValueTask HitAsync(BraidTask task, string name, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (task.State == BraidTaskState.Completed)
            {
                return;
            }

            if (task is { State: BraidTaskState.Waiting, LastProbeName: not null })
            {
                throw CreateException("Concurrent probe hit on the same worker is not supported.", null);
            }

            task.State = BraidTaskState.Waiting;
            task.LastProbeName = name;
            trace.Add($"{task.WorkerId} hit {name}");
        }

        _ = stateChanged.Release();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, shutdownCts.Token);
        await task.WaitForReleaseAsync(linkedCts.Token).ConfigureAwait(false);
    }

    public async Task JoinAsync(CancellationToken cancellationToken)
    {
        await joinMutex.WaitAsync(cancellationToken).ConfigureAwait(false);

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            lock (gate)
            {
                joined = true;
            }

            while (true)
            {
                linkedCts.Token.ThrowIfCancellationRequested();

                BraidTask? nextTask;
                var advancedWithoutRelease = false;

                lock (gate)
                {
                    var failure = tasks.FirstOrDefault(static task => task.Exception is not null)?.Exception;

                    if (failure is not null)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        throw CreateException("A forked operation failed.", failure);
                    }

                    if (tasks.Count == 0 || tasks.All(static task => task.State == BraidTaskState.Completed))
                    {
                        if (schedule is not null && nextScheduleStep < schedule.Count)
                        {
                            throw CreateException("Scripted schedule contained unused steps after all workers completed.", null);
                        }

                        break;
                    }

                    var waitingTasks = tasks.Where(static task => task.State == BraidTaskState.Waiting).OrderBy(static task => task.Id).ToArray();
                    var hasRunningTasks = tasks.Any(static task => task.State == BraidTaskState.Running);

                    nextTask = SelectNextTask(waitingTasks, hasRunningTasks, ref advancedWithoutRelease);

                    if (nextTask is not null)
                    {
                        nextTask.State = BraidTaskState.Running;
                        trace.Add(nextTask.LastProbeName is null ? $"{nextTask.WorkerId} released" : $"{nextTask.WorkerId} released at {nextTask.LastProbeName}");
                    }
                }

                if (nextTask is null)
                {
                    if (advancedWithoutRelease)
                    {
                        continue;
                    }

                    await stateChanged.WaitAsync(linkedCts.Token).ConfigureAwait(false);
                    continue;
                }

                nextTask.Release();
                await stateChanged.WaitAsync(linkedCts.Token).ConfigureAwait(false);
            }

            await WaitForRunningTasksAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            throw CreateException("braid run timed out.", ex);
        }
        catch
        {
            CancelBlockedTasks();
            await WaitForRunningTasksAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            _ = joinMutex.Release();
        }
    }

    public async Task StopAsync()
    {
        CancelBlockedTasks();
        await WaitForRunningTasksAsync().ConfigureAwait(false);
    }

    private static string FormatStep(BraidStep step) =>
        step.Kind == BraidStepKind.Hit ? $"Hit {step.WorkerId} at {step.ProbeName}" : $"{step.Kind} {step.WorkerId} at {step.ProbeName}";

    private void CancelBlockedTasks()
    {
        if (shutdownCts.IsCancellationRequested) return;
        shutdownCts.Cancel();
        _ = stateChanged.Release();
    }

    private BraidTask? SelectNextTask(BraidTask[] waitingTasks, bool hasRunningTasks, ref bool advancedWithoutRelease)
    {
        if (waitingTasks.Length > 0)
        {
            var startupTasks = waitingTasks.Where(static task => task.LastProbeName is null).ToArray();
            if (startupTasks.Length > 0)
            {
                return startupTasks[0];
            }
        }

        if (schedule is null)
        {
            return waitingTasks.Length == 0 ? null : waitingTasks[random.NextInt32(waitingTasks.Length)];
        }

        if (nextScheduleStep >= schedule.Count)
        {
            throw CreateException("Scripted schedule was exhausted before all workers completed.", null);
        }

        var step = schedule[nextScheduleStep];
        var waitingTask = waitingTasks.FirstOrDefault(task =>
            string.Equals(task.WorkerId, step.WorkerId, StringComparison.Ordinal) && string.Equals(task.LastProbeName, step.ProbeName, StringComparison.Ordinal));
        var heldTask = tasks
            .Where(static task => task.State == BraidTaskState.Held)
            .FirstOrDefault(task =>
                string.Equals(task.WorkerId, step.WorkerId, StringComparison.Ordinal) && string.Equals(task.LastProbeName, step.ProbeName, StringComparison.Ordinal));
        var sameWorkerBlockedTask = tasks.FirstOrDefault(task =>
            string.Equals(task.WorkerId, step.WorkerId, StringComparison.Ordinal) &&
            (task.State == BraidTaskState.Waiting || task.State == BraidTaskState.Held) &&
            task.LastProbeName is not null);

        switch (step.Kind)
        {
            case BraidStepKind.Hit:
            {
                var releasableTask = heldTask ?? waitingTask;
                if (releasableTask is null)
                {
                    return hasRunningTasks
                        ? null
                        : throw CreateException(
                            BuildStepMismatchMessage(nextScheduleStep, "release", step, sameWorkerBlockedTask),
                            null);
                }

                nextScheduleStep++;
                return releasableTask;
            }

            case BraidStepKind.Arrive:
            {
                if (heldTask is not null)
                {
                    throw CreateException(
                        $"Scripted schedule step {nextScheduleStep} could not be satisfied: duplicate Arrive for held {step.WorkerId} at {step.ProbeName}.",
                        null);
                }

                if (waitingTask is null)
                {
                    return hasRunningTasks
                        ? null
                        : throw CreateException(
                            BuildStepMismatchMessage(nextScheduleStep, "arrive", step, sameWorkerBlockedTask),
                            null);
                }

                waitingTask.State = BraidTaskState.Held;
                nextScheduleStep++;
                advancedWithoutRelease = true;
                trace.Add($"{waitingTask.WorkerId} arrival observed at {waitingTask.LastProbeName} (held)");
                return null;
            }

            case BraidStepKind.Release:
            {
                if (heldTask is null)
                {
                    return hasRunningTasks
                        ? null
                        : throw CreateException(
                            BuildStepMismatchMessage(nextScheduleStep, "release held", step, sameWorkerBlockedTask),
                            null);
                }

                nextScheduleStep++;
                return heldTask;
            }

            default:
                throw CreateException($"Scripted schedule step {nextScheduleStep} has unknown step kind {step.Kind}.", null);
        }
    }

    private string BuildStepMismatchMessage(int stepIndex, string action, BraidStep expectedStep, BraidTask? sameWorkerBlockedTask)
    {
        _ = iteration;
        if (sameWorkerBlockedTask?.LastProbeName is null)
        {
            return $"Scripted schedule step {stepIndex} could not be satisfied: {action} {expectedStep.WorkerId} at {expectedStep.ProbeName}.";
        }

        return $"Scripted schedule step {stepIndex} could not be satisfied: {action} {expectedStep.WorkerId} at {expectedStep.ProbeName}; actual probe is {sameWorkerBlockedTask.LastProbeName}.";
    }

    private string AppendReplayState(string message)
    {
        if (schedule is null)
        {
            return message;
        }

        var details = new List<string>
        {
            message,
            $"Next replay step: {nextScheduleStep + 1} of {schedule.Count}",
        };

        if (nextScheduleStep < schedule.Count)
        {
            details.Add($"Next replay operation: {FormatStep(schedule[nextScheduleStep])}");
        }

        var unusedSteps = schedule.Count - nextScheduleStep;
        if (unusedSteps > 0)
        {
            details.Add($"Unused replay steps: {unusedSteps}");
        }

        var heldWorkers = tasks.Where(static task => task is { State: BraidTaskState.Held, LastProbeName: not null }).OrderBy(static task => task.Id)
                               .Select(static task => $"{task.WorkerId} at {task.LastProbeName}").ToArray();
        if (heldWorkers.Length > 0)
        {
            details.Add($"Held workers: {string.Join(", ", heldWorkers)}");
        }

        var waitingWorkers = tasks.Where(static task => task is { State: BraidTaskState.Waiting, LastProbeName: not null }).OrderBy(static task => task.Id)
                                  .Select(static task => $"{task.WorkerId} at {task.LastProbeName}").ToArray();
        if (waitingWorkers.Length > 0)
        {
            details.Add($"Waiting workers: {string.Join(", ", waitingWorkers)}");
        }

        return string.Join(Environment.NewLine, details);
    }

    private async Task WaitForRunningTasksAsync()
    {
        Task[] runningTasks;

        lock (gate)
        {
            runningTasks = [.. tasks.Select(static task => task.RunningTask).OfType<Task>()];
        }

        if (runningTasks.Length == 0)
        {
            return;
        }

        var all = Task.WhenAll(runningTasks);
        if (shutdownCts.IsCancellationRequested)
        {
            var completed = await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None)).ConfigureAwait(false);
            if (completed == all)
            {
                await all.ConfigureAwait(false);
            }

            return;
        }

        await all.ConfigureAwait(false);
    }
}
