namespace Braid.Internal;

internal sealed class Scheduler : IDisposable
{
    private static readonly TimeSpan ShutdownDrainTimeout = TimeSpan.FromSeconds(1);
    private readonly Lock _gate = new();
    private readonly int _iteration;
    private readonly SemaphoreSlim _joinMutex = new(1, 1);
    private readonly DeterministicRandom _random;
    private readonly List<Task> _runningForkTasks = [];
    private readonly IReadOnlyList<BraidStep>? _schedule;
    private readonly int _seed;
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly SemaphoreSlim _stateChanged = new(0);
    private readonly List<RunTask> _tasks = [];
    private readonly TimeSpan _timeout;
    private readonly List<string> _trace = [];
    private bool _joined;
    private int _nextScheduleStep;
    private int _nextTaskId;

    public Scheduler(int seed, int iteration, TimeSpan timeout, IReadOnlyList<BraidStep>? schedule)
    {
        _seed = seed;
        _iteration = iteration;
        _timeout = timeout;
        _schedule = schedule;
        _random = new DeterministicRandom(seed);
    }

    public BraidRunException CreateException(string message, Exception? innerException, BraidRunFailureOrigin failureOrigin = BraidRunFailureOrigin.Scheduler)
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

        return new BraidRunException(resolvedMessage, _seed, _iteration, traceSnapshot, scheduleSnapshot, innerException, diagnostics, failureOrigin);
    }

    public void Fork(Func<Task> operation) => Fork(null, operation);

    public void Fork(string? workerId, Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        RunTask braidTask;
        lock (_gate)
        {
            if (_joined)
                throw CreateException("Cannot fork after JoinAsync has started.", null);

            braidTask = new RunTask(++_nextTaskId, workerId);
            _tasks.Add(braidTask);
            _trace.Add($"{braidTask.WorkerId} forked");
        }

        var registration = new ForkOperationRegistration();
        var forkTask = Task.Factory.StartNew(
            RunForkedOperationAsync,
            (braidTask, operation, registration),
            CancellationToken.None,
            TaskCreationOptions.DenyChildAttach,
            TaskScheduler.Default).Unwrap();

        lock (_gate)
        {
            registration.ForkTask = forkTask;
            if (!forkTask.IsCompleted)
                _runningForkTasks.Add(forkTask);
        }
    }

    public async ValueTask HitAsync(RunTask task, string name, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (task.State is RunTaskState.Completed)
                return;

            if (task.ProbeWaitInFlight)
                throw CreateException("Concurrent probe hit on the same worker is not supported.", null);

            task.ProbeWaitInFlight = true;
            task.State = RunTaskState.Waiting;
            task.LastProbeName = name;
            task.ProbeNames.Add(name);
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
                task.ProbeWaitInFlight = false;
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
                _joined = true;

            await RunJoinSchedulerLoopAsync(cancellationToken, linkedCts.Token).ConfigureAwait(false);
            await WaitForRunningTasksAsync().ConfigureAwait(false);

            Exception? failure;
            lock (_gate)
                failure = SchedulerSearch.FindFirstFailedException(_tasks);
            if (failure != null)
                throw CreateException("A forked operation failed.", failure, BraidRunFailureOrigin.UserTest);
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
        RunTask[] tasks;
        lock (_gate)
            tasks = [.. _tasks];

        if (!_shutdownCts.IsCancellationRequested)
            _shutdownCts.Cancel();

        // All disposed resources tolerate repeated Dispose calls, so multiple
        // calls to Dispose are safe (the scheduler is not reused after dispose).
        _shutdownCts.Dispose();
        _stateChanged.Dispose();
        _joinMutex.Dispose();

        for (var index = 0; index < tasks.Length; index++)
            tasks[index].Dispose();
    }

    internal IReadOnlyList<string> GetTraceSnapshot()
    {
        lock (_gate)
            return [.. _trace];
    }

    internal Dictionary<string, List<string>> GetWorkerProbeSequences()
    {
        lock (_gate)
        {
            var sequences = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            for (var index = 0; index < _tasks.Count; index++)
            {
                var task = _tasks[index];
                if (task.ProbeNames.Count == 0)
                    continue;

                if (!sequences.TryGetValue(task.WorkerId, out var probes))
                {
                    probes = [];
                    sequences[task.WorkerId] = probes;
                }

                probes.AddRange(task.ProbeNames);
            }

            return sequences;
        }
    }

    private static string FormatStep(BraidStep step) =>
        step.Kind is BraidStepKind.Hit ? $"Hit {step.WorkerId} at {step.ProbeName}" : $"{step.Kind} {step.WorkerId} at {step.ProbeName}";

    private bool AllJoinWorkCompleted()
    {
        if (!_tasks.TrueForAll(static task => task.State == RunTaskState.Completed))
            return false;

        if (_schedule is not null && _nextScheduleStep < _schedule.Count)
            throw CreateException(
                _tasks.Count == 0
                    ? "Scripted schedule contained unused steps, but no workers were forked."
                    : "Scripted schedule contained unused steps after all workers completed.",
                null);

        return true;
    }

    private string AppendReplayState(string message)
    {
        if (_schedule is null)
            return message;

        var details = new List<string>
        {
            message,
            $"Next replay step: {_nextScheduleStep + 1} of {_schedule.Count}",
        };

        if (_nextScheduleStep < _schedule.Count)
            details.Add($"Next replay operation: {FormatStep(_schedule[_nextScheduleStep])}");

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

        var waiting = SchedulerSearch.CollectProbeWaitDiagnostics(_tasks, RunTaskState.Waiting);
        var held = SchedulerSearch.CollectProbeWaitDiagnostics(_tasks, RunTaskState.Held);

        (int OneBasedIndex, BraidStep Step)[] unused;
        if (hasReplay && _nextScheduleStep < _schedule!.Count)
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

    private async Task CancelBlockedTasksAsync()
    {
        if (_shutdownCts.IsCancellationRequested)
            return;

        await _shutdownCts.CancelAsync().ConfigureAwait(false);
        _ = _stateChanged.Release();
    }

    private void CompleteForkedOperation(RunTask braidTask)
    {
        RunScope.CurrentTask = null;

        lock (_gate)
        {
            braidTask.State = RunTaskState.Completed;
            _trace.Add($"{braidTask.WorkerId} completed");
        }

        _ = _stateChanged.Release();
    }

    private async Task RunForkedOperationAsync(object? state)
    {
        if (state is not (RunTask braidTask, Func<Task> operation, ForkOperationRegistration registration))
            throw new InvalidOperationException("Invalid fork state.");

        RunScope.CurrentTask = braidTask;
        try
        {
            await braidTask.WaitForReleaseAsync(_shutdownCts.Token).ConfigureAwait(false);
            var opTask = Task.Factory.StartNew(
                SchedulerSearch.InvokeForkOperationAsync,
                operation,
                CancellationToken.None,
                TaskCreationOptions.DenyChildAttach,
                TaskScheduler.Default).Unwrap();
            await opTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            if (opTask.IsCanceled)
            {
                try
                {
                    await opTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException operationCanceled)
                {
                    braidTask.Exception = operationCanceled is TaskCanceledException
                        ? new OperationCanceledException(operationCanceled.Message, operationCanceled)
                        : operationCanceled;
                }
            }
            else if (opTask.IsFaulted)
            {
                braidTask.Exception = opTask.Exception.GetBaseException();
            }
        }
        catch (OperationCanceledException)
        {
            braidTask.Exception = new OperationCanceledException();
        }
        finally
        {
            lock (_gate)
                _ = _runningForkTasks.Remove(registration.ForkTask);

            CompleteForkedOperation(braidTask);
        }
    }

    private async Task RunJoinSchedulerLoopAsync(CancellationToken cancellationToken, CancellationToken linkedToken)
    {
        var matching = new BraidSchedulerMatching(this);
        while (true)
        {
            linkedToken.ThrowIfCancellationRequested();

            RunTask? nextTask;
            var advancedWithoutRelease = false;

            lock (_gate)
            {
                nextTask = matching.TrySelectNextJoinTask(cancellationToken, ref advancedWithoutRelease);
                if (nextTask == null && advancedWithoutRelease)
                    continue;

                if (nextTask == null && AllJoinWorkCompleted())
                    return;

                if (nextTask != null)
                {
                    nextTask.State = RunTaskState.Running;
                    _trace.Add(nextTask.LastProbeName == null ? $"{nextTask.WorkerId} released" : $"{nextTask.WorkerId} released at {nextTask.LastProbeName}");
                }
            }

            if (nextTask == null)
            {
                await _stateChanged.WaitAsync(linkedToken).ConfigureAwait(false);
                continue;
            }

            nextTask.Release();
            await _stateChanged.WaitAsync(linkedToken).ConfigureAwait(false);
        }
    }

    private async Task WaitForRunningTasksAsync()
    {
        Task[] runningTasks;

        lock (_gate)
        {
            if (_runningForkTasks.Count == 0)
            {
                runningTasks = [];
            }
            else
            {
                runningTasks = new Task[_runningForkTasks.Count];
                for (var index = 0; index < _runningForkTasks.Count; index++)
                    runningTasks[index] = _runningForkTasks[index];
            }
        }

        if (runningTasks.Length == 0)
            return;

        var all = Task.WhenAll(runningTasks);
        if (_shutdownCts.IsCancellationRequested)
        {
            var completed = await Task.WhenAny(all, Task.Delay(ShutdownDrainTimeout, TimeProvider.System, CancellationToken.None)).ConfigureAwait(false);
            if (completed == all)
                await all.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

            return;
        }

        await all.ConfigureAwait(false);
    }

    private sealed class BraidSchedulerMatching
    {
        private readonly Scheduler _scheduler;

        public BraidSchedulerMatching(Scheduler scheduler)
        {
            _scheduler = scheduler;
        }

        internal RunTask? TrySelectNextJoinTask(CancellationToken cancellationToken, ref bool advancedWithoutRelease)
        {
            var failure = SchedulerSearch.FindFirstFailedException(_scheduler._tasks);
            if (failure != null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw _scheduler.CreateException("A forked operation failed.", failure, BraidRunFailureOrigin.UserTest);
            }

            if (_scheduler._tasks.Count == 0 || _scheduler._tasks.TrueForAll(static task => task.State is RunTaskState.Completed))
                return null;

            var waitingTasks = SchedulerSearch.CollectWaitingTasksSortedById(_scheduler._tasks);
            var hasRunningTasks = _scheduler._tasks.Exists(static task => task.State is RunTaskState.Running);
            return SelectNextTask(waitingTasks, hasRunningTasks, ref advancedWithoutRelease);
        }

        private static string BuildStepMismatchMessage(int stepIndex, string action, BraidStep expectedStep, RunTask? sameWorkerBlockedTask)
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

        private RunTask? SelectArriveStep(BraidStep step, RunTask? waitingTask, RunTask? sameWorkerBlockedTask, bool hasRunningTasks, ref bool advancedWithoutRelease)
        {
            if (waitingTask == null)
                return hasRunningTasks ? null : throw _scheduler.CreateException(
                    BuildStepMismatchMessage(_scheduler._nextScheduleStep, "arrive", step, sameWorkerBlockedTask),
                    null);

            waitingTask.State = RunTaskState.Held;
            _scheduler._nextScheduleStep++;
            advancedWithoutRelease = true;
            _scheduler._trace.Add($"{waitingTask.WorkerId} arrival observed at {waitingTask.LastProbeName} (held)");
            return null;
        }

        private RunTask? SelectHitStep(BraidStep step, RunTask? waitingTask, RunTask? heldTask, RunTask? sameWorkerBlockedTask, bool hasRunningTasks)
        {
            var releasableTask = heldTask ?? waitingTask;
            if (releasableTask == null)
                return hasRunningTasks ? null : throw _scheduler.CreateException(
                    BuildStepMismatchMessage(_scheduler._nextScheduleStep, "hit", step, sameWorkerBlockedTask),
                    null);

            _scheduler._nextScheduleStep++;
            return releasableTask;
        }

        private RunTask? SelectNextTask(RunTask[] waitingTasks, bool hasRunningTasks, ref bool advancedWithoutRelease)
        {
            var startupTask = TrySelectStartupTask(waitingTasks);
            if (startupTask != null)
                return startupTask;

            if (_scheduler._schedule is null)
                return waitingTasks.Length == 0 ? null : waitingTasks[_scheduler._random.NextInt32(waitingTasks.Length)];

            if (_scheduler._nextScheduleStep >= _scheduler._schedule.Count)
                throw _scheduler.CreateException("Scripted schedule was exhausted before all workers completed.", null);

            return SelectScheduledTask(waitingTasks, hasRunningTasks, ref advancedWithoutRelease);
        }

        private RunTask? SelectReleaseStep(BraidStep step, RunTask? heldTask, RunTask? sameWorkerBlockedTask, bool hasRunningTasks)
        {
            if (heldTask == null)
                return hasRunningTasks ? null : throw _scheduler.CreateException(
                    BuildStepMismatchMessage(_scheduler._nextScheduleStep, "release held", step, sameWorkerBlockedTask),
                    null);

            _scheduler._nextScheduleStep++;
            return heldTask;
        }

        private RunTask? SelectScheduledTask(RunTask[] waitingTasks, bool hasRunningTasks, ref bool advancedWithoutRelease)
        {
            var step = _scheduler._schedule![_scheduler._nextScheduleStep];
            var waitingTask = SchedulerSearch.FindWaitingTask(waitingTasks, step.WorkerId, step.ProbeName);
            var heldTask = SchedulerSearch.FindHeldTask(_scheduler._tasks, step.WorkerId, step.ProbeName);
            var sameWorkerBlockedTask = SchedulerSearch.FindSameWorkerBlockedTask(_scheduler._tasks, step.WorkerId);

            return step.Kind switch
            {
                BraidStepKind.Hit => SelectHitStep(step, waitingTask, heldTask, sameWorkerBlockedTask, hasRunningTasks),
                BraidStepKind.Arrive when heldTask != null => throw _scheduler.CreateException(
                    $"Scripted schedule step {_scheduler._nextScheduleStep + 1} could not be satisfied: duplicate Arrive for held {step.WorkerId} at {step.ProbeName}.",
                    null),
                BraidStepKind.Arrive => SelectArriveStep(step, waitingTask, sameWorkerBlockedTask, hasRunningTasks, ref advancedWithoutRelease),
                BraidStepKind.Release => SelectReleaseStep(step, heldTask, sameWorkerBlockedTask, hasRunningTasks),
                _ => throw _scheduler.CreateException($"Scripted schedule step {_scheduler._nextScheduleStep + 1} has unknown step kind {step.Kind}.", null),
            };
        }
    }

    private sealed class ForkOperationRegistration
    {
        public Task ForkTask { get; set; } = Task.CompletedTask;
    }
}
