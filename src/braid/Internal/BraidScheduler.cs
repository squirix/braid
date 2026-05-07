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

        lock (gate)
        {
            traceSnapshot = [.. trace];
            scheduleSnapshot = schedule?.ToArray() ?? [];
        }

        return new BraidRunException(message, seed, iteration, traceSnapshot, scheduleSnapshot, innerException);
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
                var opTask = operation();
                if (opTask is null)
                {
                    throw new InvalidOperationException("Fork operation returned a null task.");
                }

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

            if (task.State == BraidTaskState.Waiting && task.LastProbeName is not null)
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

                    nextTask = SelectNextTask(waitingTasks, hasRunningTasks);

                    if (nextTask is not null)
                    {
                        nextTask.State = BraidTaskState.Running;
                        trace.Add(nextTask.LastProbeName is null ? $"{nextTask.WorkerId} released" : $"{nextTask.WorkerId} released at {nextTask.LastProbeName}");
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

    private void CancelBlockedTasks()
    {
        if (shutdownCts.IsCancellationRequested) return;
        shutdownCts.Cancel();
        _ = stateChanged.Release();
    }

    private BraidTask? SelectNextTask(BraidTask[] waitingTasks, bool hasRunningTasks)
    {
        if (waitingTasks.Length == 0)
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
            return waitingTasks[random.NextInt32(waitingTasks.Length)];
        }

        if (nextScheduleStep >= schedule.Count)
        {
            throw CreateException("Scripted schedule was exhausted before all workers completed.", null);
        }

        var step = schedule[nextScheduleStep];
        var task = waitingTasks.FirstOrDefault(task =>
            string.Equals(task.WorkerId, step.WorkerId, StringComparison.Ordinal) && string.Equals(task.LastProbeName, step.ProbeName, StringComparison.Ordinal));

        if (task is null)
        {
            return hasRunningTasks ? null : throw CreateException($"Scripted schedule step {nextScheduleStep} could not be satisfied: release {step.WorkerId} at {step.ProbeName}.", null);
        }

        nextScheduleStep++;
        return task;
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
