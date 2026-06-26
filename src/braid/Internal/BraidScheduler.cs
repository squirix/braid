namespace Braid.Internal;

internal sealed class BraidScheduler : IDisposable
{
    private readonly Lock _gate = new();
    private readonly int _iteration;
    private readonly SemaphoreSlim _joinMutex = new(1, 1);
    private readonly DeterministicRandom _random;
    private readonly List<Task> _runningForkTasks = [];
    private readonly IReadOnlyList<BraidStep>? _schedule;
    private readonly int _seed;
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly SemaphoreSlim _stateChanged = new(0);
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
        BraidSchedulerDiagnostics diagnostics;

        lock (_gate)
        {
            traceSnapshot = [.. _trace];
            scheduleSnapshot = _schedule is null ? [] : [.. _schedule];
            resolvedMessage = AppendReplayState(message);
            diagnostics = BuildDiagnosticSnapshot();
        }

        return new BraidRunException(resolvedMessage, _seed, _iteration, traceSnapshot, scheduleSnapshot, innerException, diagnostics);
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

        _ = Task.Factory.StartNew(RunForkedOperationAsync, (braidTask, operation), _shutdownCts.Token, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default).Unwrap();
    }

    public async ValueTask HitAsync(BraidTask task, string name, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (task.State is BraidTaskState.Completed)
            {
                return;
            }

            if (task.ProbeWaitInFlight)
            {
                throw CreateException("Concurrent probe hit on the same worker is not supported.", null);
            }

            task.ProbeWaitInFlight = true;
            task.State = BraidTaskState.Waiting;
            task.LastProbeName = name;
            _trace.Add($"{task.WorkerId} hit {name}");
        }

        _ = _stateChanged.Release();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdownCts.Token);
        try
        {
            await task.WaitForReleaseAsync(linkedCts.Token).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                task.ProbeWaitInFlight = false;
            }
        }
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

            await RunJoinSchedulerLoopAsync(cancellationToken, linkedCts.Token).ConfigureAwait(false);
            await WaitForRunningTasksAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            throw CreateException("braid run timed out.", ex);
        }
        catch
        {
            await CancelBlockedTasksAsync().ConfigureAwait(false);
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
        await CancelBlockedTasksAsync().ConfigureAwait(false);
        await WaitForRunningTasksAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        _shutdownCts.Dispose();
        _stateChanged.Dispose();
        _joinMutex.Dispose();

        for (var index = 0; index < _tasks.Count; index++)
        {
            _tasks[index].Dispose();
        }
    }

    private static string FormatStep(BraidStep step) =>
        step.Kind is BraidStepKind.Hit ? $"Hit {step.WorkerId} at {step.ProbeName}" : $"{step.Kind} {step.WorkerId} at {step.ProbeName}";

    private static BraidTask? TrySelectStartupTask(BraidTask[] waitingTasks)
    {
        for (var index = 0; index < waitingTasks.Length; index++)
        {
            var task = waitingTasks[index];
            if (task.LastProbeName is null)
            {
                return task;
            }
        }

        return null;
    }

    private bool AllJoinWorkCompleted()
    {
        if (_tasks.Count is not 0 && !_tasks.TrueForAll(static task => task.State is BraidTaskState.Completed))
        {
            return false;
        }

        if (_schedule is not null && _nextScheduleStep < _schedule.Count)
        {
            throw CreateException("Scripted schedule contained unused steps after all workers completed.", null);
        }

        return true;
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

        return string.Join(Environment.NewLine, details);
    }

    private BraidSchedulerDiagnostics BuildDiagnosticSnapshot()
    {
        var hasReplay = _schedule?.Count > 0;

        BraidStep? lastMatched = null;
        int? lastMatchedOneBased = null;
        if (hasReplay && _nextScheduleStep > 0)
        {
            lastMatched = _schedule![_nextScheduleStep - 1];
            lastMatchedOneBased = _nextScheduleStep;
        }

        var waiting = BraidSchedulerSearch.CollectProbeWaitDiagnostics(_tasks, BraidTaskState.Waiting);
        var held = BraidSchedulerSearch.CollectProbeWaitDiagnostics(_tasks, BraidTaskState.Held);

        (int OneBasedIndex, BraidStep Step)[] unused;
        if (hasReplay && _schedule is not null && _nextScheduleStep < _schedule.Count)
        {
            var remaining = _schedule.Count - _nextScheduleStep;
            unused = new (int, BraidStep)[remaining];
            for (var index = 0; index < remaining; index++)
            {
                var scheduleIndex = _nextScheduleStep + index;
                unused[index] = (scheduleIndex + 1, _schedule[scheduleIndex]);
            }
        }
        else
        {
            unused = [];
        }

        return new BraidSchedulerDiagnostics(hasReplay, lastMatched, lastMatchedOneBased, waiting, held, unused);
    }

    private string BuildStepMismatchMessage(int stepIndex, string action, BraidStep expectedStep, BraidTask? sameWorkerBlockedTask)
    {
        _ = _iteration;
        return sameWorkerBlockedTask?.LastProbeName is null
            ? $"Scripted schedule step {stepIndex} could not be satisfied: {action} {expectedStep.WorkerId} at {expectedStep.ProbeName}."
            : $"Scripted schedule step {stepIndex} could not be satisfied: {action} {expectedStep.WorkerId} at {expectedStep.ProbeName}; actual probe is {sameWorkerBlockedTask.LastProbeName}.";
    }

    private async Task CancelBlockedTasksAsync()
    {
        if (_shutdownCts.IsCancellationRequested)
        {
            return;
        }

        await _shutdownCts.CancelAsync().ConfigureAwait(false);
        _ = _stateChanged.Release();
    }

    private void CompleteForkedOperation(BraidTask braidTask)
    {
        BraidRunScope.CurrentTask = null;

        lock (_gate)
        {
            braidTask.State = BraidTaskState.Completed;
            _trace.Add($"{braidTask.WorkerId} completed");
        }

        _ = _stateChanged.Release();
    }

    private async Task ExecuteForkWorkerAsync(BraidTask braidTask, Func<Task> operation)
    {
        await braidTask.WaitForReleaseAsync(_shutdownCts.Token).ConfigureAwait(false);
        var opTask = operation() ?? throw new InvalidOperationException("Fork operation returned a null task.");
        await opTask.ConfigureAwait(false);
    }

    private Task RunForkedOperationAsync(object? state)
    {
        if (state is not (BraidTask braidTask, Func<Task> operation))
        {
            throw new InvalidOperationException("Invalid fork state.");
        }

        BraidRunScope.CurrentTask = braidTask;
        var workerTask = ExecuteForkWorkerAsync(braidTask, operation);

        lock (_gate)
        {
            _runningForkTasks.Add(workerTask);
        }

        var forkTask = workerTask.ContinueWith(
            completed =>
            {
                if (completed.IsFaulted)
                {
                    braidTask.Exception = completed.Exception!.GetBaseException();
                }
                else if (completed.IsCanceled)
                {
                    braidTask.Exception = new OperationCanceledException();
                }

                lock (_gate)
                {
                    _ = _runningForkTasks.Remove(workerTask);
                }

                CompleteForkedOperation(braidTask);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        lock (_gate)
        {
            _ = _runningForkTasks.Remove(workerTask);
            if (!forkTask.IsCompleted)
            {
                _runningForkTasks.Add(forkTask);
            }
        }

        return forkTask;
    }

    private async Task RunJoinSchedulerLoopAsync(CancellationToken cancellationToken, CancellationToken linkedToken)
    {
        while (true)
        {
            linkedToken.ThrowIfCancellationRequested();

            BraidTask? nextTask;
            var advancedWithoutRelease = false;

            lock (_gate)
            {
                nextTask = TrySelectNextJoinTask(cancellationToken, ref advancedWithoutRelease);
                if (nextTask is null && advancedWithoutRelease)
                {
                    continue;
                }

                if (nextTask is null && AllJoinWorkCompleted())
                {
                    return;
                }

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

                await _stateChanged.WaitAsync(linkedToken).ConfigureAwait(false);
                continue;
            }

            nextTask.Release();
            await _stateChanged.WaitAsync(linkedToken).ConfigureAwait(false);
        }
    }

    private BraidTask? SelectArriveStep(
        BraidStep step,
        BraidTask? waitingTask,
        BraidTask? heldTask,
        BraidTask? sameWorkerBlockedTask,
        bool hasRunningTasks,
        ref bool advancedWithoutRelease)
    {
        if (heldTask is not null)
        {
            throw CreateException($"Scripted schedule step {_nextScheduleStep} could not be satisfied: duplicate Arrive for held {step.WorkerId} at {step.ProbeName}.", null);
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

    private BraidTask? SelectHitStep(BraidStep step, BraidTask? waitingTask, BraidTask? heldTask, BraidTask? sameWorkerBlockedTask, bool hasRunningTasks)
    {
        var releasableTask = heldTask ?? waitingTask;
        if (releasableTask is null)
        {
            return hasRunningTasks ? null : throw CreateException(BuildStepMismatchMessage(_nextScheduleStep, "release", step, sameWorkerBlockedTask), null);
        }

        _nextScheduleStep++;
        return releasableTask;
    }

    private BraidTask? SelectNextTask(BraidTask[] waitingTasks, bool hasRunningTasks, ref bool advancedWithoutRelease)
    {
        var startupTask = TrySelectStartupTask(waitingTasks);
        if (startupTask is not null)
        {
            return startupTask;
        }

        if (_schedule is null)
        {
            return waitingTasks.Length is 0 ? null : waitingTasks[_random.NextInt32(waitingTasks.Length)];
        }

        if (_nextScheduleStep >= _schedule.Count)
        {
            throw CreateException("Scripted schedule was exhausted before all workers completed.", null);
        }

        return SelectScheduledTask(waitingTasks, hasRunningTasks, ref advancedWithoutRelease);
    }

    private BraidTask? SelectReleaseStep(BraidStep step, BraidTask? heldTask, BraidTask? sameWorkerBlockedTask, bool hasRunningTasks)
    {
        if (heldTask is null)
        {
            return hasRunningTasks ? null : throw CreateException(BuildStepMismatchMessage(_nextScheduleStep, "release held", step, sameWorkerBlockedTask), null);
        }

        _nextScheduleStep++;
        return heldTask;
    }

    private BraidTask? SelectScheduledTask(BraidTask[] waitingTasks, bool hasRunningTasks, ref bool advancedWithoutRelease)
    {
        var step = _schedule![_nextScheduleStep];
        var waitingTask = BraidSchedulerSearch.FindWaitingTask(waitingTasks, step.WorkerId, step.ProbeName);
        var heldTask = BraidSchedulerSearch.FindHeldTask(_tasks, step.WorkerId, step.ProbeName);
        var sameWorkerBlockedTask = BraidSchedulerSearch.FindSameWorkerBlockedTask(_tasks, step.WorkerId);

        return step.Kind switch
        {
            BraidStepKind.Hit => SelectHitStep(step, waitingTask, heldTask, sameWorkerBlockedTask, hasRunningTasks),
            BraidStepKind.Arrive => SelectArriveStep(step, waitingTask, heldTask, sameWorkerBlockedTask, hasRunningTasks, ref advancedWithoutRelease),
            BraidStepKind.Release => SelectReleaseStep(step, heldTask, sameWorkerBlockedTask, hasRunningTasks),
            _ => throw CreateException($"Scripted schedule step {_nextScheduleStep} has unknown step kind {step.Kind}.", null),
        };
    }

    private BraidTask? TrySelectNextJoinTask(CancellationToken cancellationToken, ref bool advancedWithoutRelease)
    {
        var failure = BraidSchedulerSearch.FindFirstFailedException(_tasks);
        if (failure is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw CreateException("A forked operation failed.", failure);
        }

        if (_tasks.Count is 0 || _tasks.TrueForAll(static task => task.State is BraidTaskState.Completed))
        {
            return null;
        }

        var waitingTasks = BraidSchedulerSearch.CollectWaitingTasksSortedById(_tasks);
        var hasRunningTasks = _tasks.Exists(static task => task.State is BraidTaskState.Running);
        return SelectNextTask(waitingTasks, hasRunningTasks, ref advancedWithoutRelease);
    }

    private async Task WaitForRunningTasksAsync()
    {
        Task[] runningTasks;

        lock (_gate)
        {
            if (_runningForkTasks.Count is 0)
            {
                runningTasks = [];
            }
            else
            {
                runningTasks = new Task[_runningForkTasks.Count];
                for (var index = 0; index < _runningForkTasks.Count; index++)
                {
                    runningTasks[index] = _runningForkTasks[index];
                }
            }
        }

        if (runningTasks.Length is 0)
        {
            return;
        }

        var all = Task.WhenAll(runningTasks);
        if (_shutdownCts.IsCancellationRequested)
        {
            var completed = await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(1), TimeProvider.System, CancellationToken.None)).ConfigureAwait(false);
            if (completed == all)
            {
                await all.ConfigureAwait(false);
            }

            return;
        }

        await all.ConfigureAwait(false);
    }
}
