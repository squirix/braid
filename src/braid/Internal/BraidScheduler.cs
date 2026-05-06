namespace Braid.Internal;

internal sealed class BraidScheduler
{
    private readonly Lock gate = new();
    private readonly int iteration;
    private readonly DeterministicRandom random;
    private readonly IReadOnlyList<BraidStep>? schedule;
    private readonly int seed;
    private readonly CancellationTokenSource shutdownCts = new();
    private readonly SemaphoreSlim stateChanged = new(0);
    private readonly List<BraidTask> tasks = [];
    private readonly TimeSpan timeout;
    private readonly List<string> trace = [];
    private bool joined;
    private int nextTaskId;
    private int nextScheduleStep;

    internal BraidScheduler(int seed, int iteration, TimeSpan timeout, IReadOnlyList<BraidStep>? schedule)
    {
        this.seed = seed;
        this.iteration = iteration;
        this.timeout = timeout;
        this.schedule = schedule;
        random = new DeterministicRandom(seed);
    }

    internal BraidRunException CreateException(string message, Exception? innerException)
    {
        IReadOnlyList<string> traceSnapshot;

        lock (gate)
        {
            traceSnapshot = trace.ToArray();
        }

        return new BraidRunException(message, seed, iteration, traceSnapshot, innerException);
    }

    internal void Fork(Func<Task> operation)
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
                await operation().ConfigureAwait(false);
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

    internal async ValueTask HitAsync(BraidTask task, string name, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (task.State == BraidTaskState.Completed)
            {
                return;
            }

            task.State = BraidTaskState.Waiting;
            task.LastProbeName = name;
            trace.Add($"{task.WorkerId} hit {name}");
        }

        _ = stateChanged.Release();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, shutdownCts.Token);
        await task.WaitForReleaseAsync(linkedCts.Token).ConfigureAwait(false);
    }

    internal async Task JoinAsync(CancellationToken cancellationToken)
    {
        try
        {
            lock (gate)
            {
                joined = true;
            }

            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            while (true)
            {
                linkedCts.Token.ThrowIfCancellationRequested();

                BraidTask? nextTask;

                lock (gate)
                {
                    var failure = tasks.FirstOrDefault(static task => task.Exception is not null)?.Exception;

                    if (failure is not null)
                    {
                        throw CreateException("A forked operation failed.", failure);
                    }

                    if (tasks.Count == 0 || tasks.All(static task => task.State == BraidTaskState.Completed))
                    {
                        break;
                    }

                    var waitingTasks = tasks.Where(static task => task.State == BraidTaskState.Waiting).OrderBy(static task => task.Id).ToArray();
                    var hasRunningTasks = tasks.Any(static task => task.State == BraidTaskState.Running);

                    nextTask = SelectNextTask(waitingTasks, hasRunningTasks);

                    if (nextTask is not null)
                    {
                        nextTask.State = BraidTaskState.Running;
                        trace.Add($"{nextTask.WorkerId} released");
                    }
                }

                if (nextTask is null)
                {
                    await stateChanged.WaitAsync(linkedCts.Token).ConfigureAwait(false);
                    continue;
                }

                nextTask.Release();
                await stateChanged.WaitAsync(linkedCts.Token).ConfigureAwait(false);
            }

            await WaitForRunningTasksAsync().ConfigureAwait(false);
        }
        catch
        {
            CancelBlockedTasks();
            await WaitForRunningTasksAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal async Task StopAsync()
    {
        CancelBlockedTasks();
        await WaitForRunningTasksAsync().ConfigureAwait(false);
    }

    private void CancelBlockedTasks()
    {
        if (shutdownCts.IsCancellationRequested) return;
        shutdownCts.Cancel();
        _ = stateChanged.Release();
    }

    private BraidTask? SelectNextTask(IReadOnlyList<BraidTask> waitingTasks, bool hasRunningTasks)
    {
        if (waitingTasks.Count == 0)
        {
            return null;
        }

        var startupTasks = waitingTasks.Where(static task => task.LastProbeName is null).ToArray();
        if (startupTasks.Length > 0)
        {
            return startupTasks[0];
        }

        if (schedule is null)
        {
            return waitingTasks[random.NextInt32(waitingTasks.Count)];
        }

        if (nextScheduleStep >= schedule.Count)
        {
            throw CreateException("Scripted schedule was exhausted before all workers completed.", null);
        }

        var step = schedule[nextScheduleStep];
        var task = waitingTasks.FirstOrDefault(task =>
            string.Equals(task.WorkerId, step.WorkerId, StringComparison.Ordinal) &&
            string.Equals(task.LastProbeName, step.ProbeName, StringComparison.Ordinal));

        if (task is null)
        {
            if (hasRunningTasks)
            {
                return null;
            }

            throw CreateException($"Scripted schedule step {nextScheduleStep} could not be satisfied: release {step.WorkerId} at {step.ProbeName}.", null);
        }

        nextScheduleStep++;
        return task;
    }

    private async Task WaitForRunningTasksAsync()
    {
        Task[] runningTasks;

        lock (gate)
        {
            runningTasks = tasks.Select(static task => task.RunningTask).OfType<Task>().ToArray();
        }

        await Task.WhenAll(runningTasks).ConfigureAwait(false);
    }
}
