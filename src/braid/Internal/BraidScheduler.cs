namespace Braid.Internal;

internal sealed class BraidScheduler : IDisposable
{
    private readonly Lock _gate = new();
    private readonly int _iteration;
    private readonly DeterministicRandom _random;
    private readonly IReadOnlyList<BraidStep>? _schedule;
    private readonly int _seed;
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly SemaphoreSlim _stateChanged = new(0);
    private readonly SemaphoreSlim _joinMutex = new(1, 1);
    private readonly List<BraidTask> _tasks = [];
    private readonly TimeSpan _timeout;
    private readonly List<string> _trace = [];
    private bool _joined;
    private int _nextScheduleStep;
    private int _nextTaskId;

    public BraidScheduler(int seed, int iteration, TimeSpan timeout, IReadOnlyList<BraidStep>? schedule)
    {
        _seed = seed;
        _iteration = iteration;
        _timeout = timeout;
        _schedule = schedule;
        _random = new DeterministicRandom(seed);
    }

    public BraidRunException CreateException(string message, Exception? innerException)
    {
        IReadOnlyList<string> traceSnapshot;
        IReadOnlyList<BraidStep> scheduleSnapshot;
        string resolvedMessage;

        lock (_gate)
        {
            traceSnapshot = [.. _trace];
            scheduleSnapshot = _schedule?.ToArray() ?? [];
            resolvedMessage = AppendReplayState(message);
        }

        return new BraidRunException(resolvedMessage, _seed, _iteration, traceSnapshot, scheduleSnapshot, innerException);
    }

    public void Dispose()
    {
        _shutdownCts.Dispose();
        _stateChanged.Dispose();
        _joinMutex.Dispose();

        foreach (var task in _tasks)
        {
            task.Dispose();
        }
    }

    public void Fork(Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        BraidTask braidTask;

        lock (_gate)
        {
            if (_joined)
            {
                throw CreateException("Cannot fork after JoinAsync has started.", null);
            }

            braidTask = new BraidTask(++_nextTaskId);
            _tasks.Add(braidTask);
            _trace.Add($"{braidTask.WorkerId} forked");
        }

        braidTask.RunningTask = Task.Run(async () =>
        {
            BraidRunScope.CurrentTask = braidTask;

            try
            {
                await braidTask.WaitForReleaseAsync(_shutdownCts.Token).ConfigureAwait(false);
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

                lock (_gate)
                {
                    braidTask.State = BraidTaskState.Completed;
                    _trace.Add($"{braidTask.WorkerId} completed");
                }

                _ = _stateChanged.Release();
            }
        });
    }

    public async ValueTask HitAsync(BraidTask task, string name, CancellationToken cancellationToken)
    {
        lock (_gate)
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
            _trace.Add($"{task.WorkerId} hit {name}");
        }

        _ = _stateChanged.Release();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdownCts.Token);
        await task.WaitForReleaseAsync(linkedCts.Token).ConfigureAwait(false);
    }

    public async Task JoinAsync(CancellationToken cancellationToken)
    {
        await _joinMutex.WaitAsync(cancellationToken).ConfigureAwait(false);

        using var timeoutCts = new CancellationTokenSource(_timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            lock (_gate)
            {
                _joined = true;
            }

            while (true)
            {
                linkedCts.Token.ThrowIfCancellationRequested();

                BraidTask? nextTask;
                var advancedWithoutRelease = false;

                lock (_gate)
                {
                    var failure = _tasks.FirstOrDefault(static task => task.Exception is not null)?.Exception;

                    if (failure is not null)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        throw CreateException("A forked operation failed.", failure);
                    }

                    if (_tasks.Count == 0 || _tasks.All(static task => task.State == BraidTaskState.Completed))
                    {
                        if (_schedule is not null && _nextScheduleStep < _schedule.Count)
                        {
                            throw CreateException("Scripted schedule contained unused steps after all workers completed.", null);
                        }

                        break;
                    }

                    var waitingTasks = _tasks.Where(static task => task.State == BraidTaskState.Waiting).OrderBy(static task => task.Id).ToArray();
                    var hasRunningTasks = _tasks.Any(static task => task.State == BraidTaskState.Running);

                    nextTask = SelectNextTask(waitingTasks, hasRunningTasks, ref advancedWithoutRelease);

                    if (nextTask is not null)
                    {
                        nextTask.State = BraidTaskState.Running;
                        _trace.Add(nextTask.LastProbeName is null ? $"{nextTask.WorkerId} released" : $"{nextTask.WorkerId} released at {nextTask.LastProbeName}");
                    }
                }

                if (nextTask is null)
                {
                    if (advancedWithoutRelease)
                    {
                        continue;
                    }

                    await _stateChanged.WaitAsync(linkedCts.Token).ConfigureAwait(false);
                    continue;
                }

                nextTask.Release();
                await _stateChanged.WaitAsync(linkedCts.Token).ConfigureAwait(false);
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
            _ = _joinMutex.Release();
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
        if (_shutdownCts.IsCancellationRequested) return;
        _shutdownCts.Cancel();
        _ = _stateChanged.Release();
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

        if (_schedule is null)
        {
            return waitingTasks.Length == 0 ? null : waitingTasks[_random.NextInt32(waitingTasks.Length)];
        }

        if (_nextScheduleStep >= _schedule.Count)
        {
            throw CreateException("Scripted schedule was exhausted before all workers completed.", null);
        }

        var step = _schedule[_nextScheduleStep];
        var waitingTask = waitingTasks.FirstOrDefault(task =>
            string.Equals(task.WorkerId, step.WorkerId, StringComparison.Ordinal) && string.Equals(task.LastProbeName, step.ProbeName, StringComparison.Ordinal));
        var heldTask = _tasks.Where(static task => task.State == BraidTaskState.Held).FirstOrDefault(task =>
            string.Equals(task.WorkerId, step.WorkerId, StringComparison.Ordinal) && string.Equals(task.LastProbeName, step.ProbeName, StringComparison.Ordinal));
        var sameWorkerBlockedTask = _tasks.FirstOrDefault(task =>
            string.Equals(task.WorkerId, step.WorkerId, StringComparison.Ordinal) && task.State is BraidTaskState.Waiting or BraidTaskState.Held && task.LastProbeName is not null);

        switch (step.Kind)
        {
            case BraidStepKind.Hit:
            {
                var releasableTask = heldTask ?? waitingTask;
                if (releasableTask is null)
                {
                    return hasRunningTasks ? null : throw CreateException(BuildStepMismatchMessage(_nextScheduleStep, "release", step, sameWorkerBlockedTask), null);
                }

                _nextScheduleStep++;
                return releasableTask;
            }

            case BraidStepKind.Arrive:
            {
                if (heldTask is not null)
                {
                    throw CreateException(
                        $"Scripted schedule step {_nextScheduleStep} could not be satisfied: duplicate Arrive for held {step.WorkerId} at {step.ProbeName}.",
                        null);
                }

                if (waitingTask is null)
                {
                    return hasRunningTasks ? null : throw CreateException(BuildStepMismatchMessage(_nextScheduleStep, "arrive", step, sameWorkerBlockedTask), null);
                }

                waitingTask.State = BraidTaskState.Held;
                _nextScheduleStep++;
                advancedWithoutRelease = true;
                _trace.Add($"{waitingTask.WorkerId} arrival observed at {waitingTask.LastProbeName} (held)");
                return null;
            }

            case BraidStepKind.Release:
            {
                if (heldTask is null)
                {
                    return hasRunningTasks ? null : throw CreateException(BuildStepMismatchMessage(_nextScheduleStep, "release held", step, sameWorkerBlockedTask), null);
                }

                _nextScheduleStep++;
                return heldTask;
            }

            default:
                throw CreateException($"Scripted schedule step {_nextScheduleStep} has unknown step kind {step.Kind}.", null);
        }
    }

    private string BuildStepMismatchMessage(int stepIndex, string action, BraidStep expectedStep, BraidTask? sameWorkerBlockedTask)
    {
        _ = _iteration;
        return sameWorkerBlockedTask?.LastProbeName is null
            ? $"Scripted schedule step {stepIndex} could not be satisfied: {action} {expectedStep.WorkerId} at {expectedStep.ProbeName}."
            : $"Scripted schedule step {stepIndex} could not be satisfied: {action} {expectedStep.WorkerId} at {expectedStep.ProbeName}; actual probe is {sameWorkerBlockedTask.LastProbeName}.";
    }

    private string AppendReplayState(string message)
    {
        if (_schedule is null)
        {
            return message;
        }

        var details = new List<string>
        {
            message,
            $"Next replay step: {_nextScheduleStep + 1} of {_schedule.Count}",
        };

        if (_nextScheduleStep < _schedule.Count)
        {
            details.Add($"Next replay operation: {FormatStep(_schedule[_nextScheduleStep])}");
        }

        var unusedSteps = _schedule.Count - _nextScheduleStep;
        if (unusedSteps > 0)
        {
            details.Add($"Unused replay steps: {unusedSteps}");
        }

        var heldWorkers = _tasks.Where(static task => task is { State: BraidTaskState.Held, LastProbeName: not null }).OrderBy(static task => task.Id)
                                .Select(static task => $"{task.WorkerId} at {task.LastProbeName}").ToArray();
        if (heldWorkers.Length > 0)
        {
            details.Add($"Held workers: {string.Join(", ", heldWorkers)}");
        }

        var waitingWorkers = _tasks.Where(static task => task is { State: BraidTaskState.Waiting, LastProbeName: not null }).OrderBy(static task => task.Id)
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

        lock (_gate)
        {
            runningTasks = [.. _tasks.Select(static task => task.RunningTask).OfType<Task>()];
        }

        if (runningTasks.Length == 0)
        {
            return;
        }

        var all = Task.WhenAll(runningTasks);
        if (_shutdownCts.IsCancellationRequested)
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
