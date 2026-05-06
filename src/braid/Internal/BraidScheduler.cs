namespace Braid.Internal;

internal sealed class BraidScheduler
{
    private readonly object gate = new();
    private readonly int iteration;
    private readonly DeterministicRandom random;
    private readonly int seed;
    private readonly SemaphoreSlim stateChanged = new(0);
    private readonly List<BraidTask> tasks = [];
    private readonly TimeSpan timeout;
    private readonly List<string> trace = [];
    private bool joined;
    private int nextTaskId;

    internal BraidScheduler(int seed, int iteration, TimeSpan timeout)
    {
        this.seed = seed;
        this.iteration = iteration;
        this.timeout = timeout;
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
            trace.Add($"task-{braidTask.Id} forked");
        }

        braidTask.RunningTask = Task.Run(async () =>
        {
            BraidRunScope.CurrentTask = braidTask;

            try
            {
                await braidTask.WaitForReleaseAsync(CancellationToken.None).ConfigureAwait(false);
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
                    trace.Add($"task-{braidTask.Id} completed");
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
            trace.Add($"task-{task.Id} hit {name}");
        }

        _ = stateChanged.Release();
        await task.WaitForReleaseAsync(cancellationToken).ConfigureAwait(false);
    }

    internal async Task JoinAsync(CancellationToken cancellationToken)
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
            Exception? failure;

            lock (gate)
            {
                failure = tasks.FirstOrDefault(static task => task.Exception is not null)?.Exception;

                if (failure is not null)
                {
                    throw CreateException("A forked operation failed.", failure);
                }

                if (tasks.Count > 0 && tasks.All(static task => task.State == BraidTaskState.Completed))
                {
                    break;
                }

                var waitingTasks = tasks.Where(static task => task.State == BraidTaskState.Waiting).OrderBy(static task => task.Id).ToArray();

                nextTask = waitingTasks.Length == 0 ? null : waitingTasks[random.NextInt32(waitingTasks.Length)];

                if (nextTask is not null)
                {
                    nextTask.State = BraidTaskState.Running;
                    trace.Add($"task-{nextTask.Id} released");
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

        var runningTasks = tasks.Select(static task => task.RunningTask).OfType<Task>().ToArray();
        await Task.WhenAll(runningTasks).ConfigureAwait(false);
    }
}
