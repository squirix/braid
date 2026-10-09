namespace Braid;

internal sealed class Scheduler : IDisposable
{
    private const string ParkedTimeoutMessage =
        "braid run timed out. A running worker did not reach a probe while other workers were parked before their start or at probes; it may be waiting for one of them.";

    private const string ScriptExhaustedMessage = "Scripted schedule was exhausted before all workers completed.";
    private static readonly TimeSpan ShutdownDrainTimeout = TimeSpan.FromSeconds(1);
    private readonly Lock _gate = new();
    private readonly int _iteration;
    private readonly SemaphoreSlim _joinMutex = new(1, 1);
    private readonly DeterministicRandom _random;
    private readonly RunningWorkers _runningWorkers = new();
    private readonly ReplayScript? _script;
    private readonly int _seed;
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly SemaphoreSlim _stateChanged = new(0);
    private readonly List<RunTask> _tasks = [];
    private readonly TimeSpan _timeout;
    private readonly List<string> _trace = [];
    private bool _joined;
    private int _nextScheduleStep;
    private int _nextTaskId;

    internal Scheduler(int seed, int iteration, TimeSpan timeout, IReadOnlyList<ReplayStep>? steps, bool completesInForkOrder)
    {
        _seed = seed;
        _iteration = iteration;
        _timeout = timeout;
        _script = steps == null ? null : new ReplayScript(steps, completesInForkOrder);
        _random = new DeterministicRandom(seed);
    }

    private IReadOnlyList<ReplayStep>? ScriptSteps => _script?.Steps;

    public void Dispose()
    {
        RunTask[] tasks;
        bool workersRunning;
        lock (_gate)
        {
            tasks = [.. _tasks];
            workersRunning = _runningWorkers.AnyRunning;
        }

        if (!_shutdownCts.IsCancellationRequested)
            _shutdownCts.Cancel();

        _joinMutex.Dispose();

        // A worker abandoned after the shutdown drain still uses the shutdown token, the state signal and its permit:
        // its next probe throws OperationCanceledException instead of ObjectDisposedException. The garbage collector reclaims them.
        if (workersRunning)
            return;

        _shutdownCts.Dispose();
        _stateChanged.Dispose();

        for (var index = 0; index < tasks.Length; index++)
            tasks[index].Dispose();
    }

    internal RunException CreateException(string message, Exception? innerException, RunFailureOrigin failureOrigin = RunFailureOrigin.Scheduler)
    {
        IReadOnlyList<string> traceSnapshot;
        IReadOnlyList<ReplayStep> scheduleSnapshot;
        string resolvedMessage;
        SchedulerDiagnostics diagnostics;

        lock (_gate)
        {
            traceSnapshot = [.. _trace];
            scheduleSnapshot = ScriptSteps == null ? [] : [.. ScriptSteps];
            resolvedMessage = AppendReplayState(message);
            diagnostics = BuildDiagnosticSnapshot();
        }

        return new RunException(resolvedMessage, new RunExceptionContext(_seed, _iteration, traceSnapshot, scheduleSnapshot, diagnostics), innerException, failureOrigin);
    }

    internal void Fork(Func<Task> operation) => Fork(null, operation);

    internal void Fork(string? workerId, Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        RunTask braidTask;
        lock (_gate)
        {
            if (_joined)
                throw CreateException("Cannot fork after JoinAsync has started.", null);

            braidTask = new RunTask(_nextTaskId + 1, workerId);
            ThrowIfWorkerIdInUse(braidTask.WorkerId);
            _nextTaskId++;
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
                _runningWorkers.Add(forkTask);
        }
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

    internal async ValueTask HitAsync(RunTask task, string name, CancellationToken cancellationToken)
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

    internal async Task JoinAsync(CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(_timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        // A concurrent join waits here; the run timeout bounds that wait too. Cleanup runs only after the mutex is held.
        try
        {
            await _joinMutex.WaitAsync(linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            throw CreateTimeoutException(ex);
        }

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
                throw CreateException("A forked operation failed.", failure, RunFailureOrigin.UserTest);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            throw CreateTimeoutException(ex);
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

    internal async Task StopAsync()
    {
        await CancelBlockedTasksAsync().ConfigureAwait(false);
        await WaitForRunningTasksAsync().ConfigureAwait(false);
    }

    /// <summary>Names the workers abandoned by the last shutdown drain in the message of the run failure.</summary>
    /// <param name="exception">The run failure.</param>
    internal void ReportAbandonedWorkers(RunException exception)
    {
        string? description;
        lock (_gate)
            description = _runningWorkers.DescribeAbandoned();

        if (description != null)
            exception.ReportAbandonedWorkers(description);
    }

    /// <summary>
    /// Rejects a second worker with the same id: replay steps and explored probe sequences address workers by id,
    /// so two workers with one id could not be scheduled or replayed separately.
    /// </summary>
    /// <param name="workerId">The id of the worker being forked, explicit or generated.</param>
    /// <exception cref="ArgumentException">Another worker in this run already uses <paramref name="workerId" />.</exception>
    private void ThrowIfWorkerIdInUse(string workerId)
    {
        for (var index = 0; index < _tasks.Count; index++)
        {
            if (string.Equals(_tasks[index].WorkerId, workerId, StringComparison.Ordinal))
                throw new ArgumentException($"Worker id '{workerId}' is already used in this run. Each forked worker needs a unique id.", nameof(workerId));
        }
    }

    private bool AllJoinWorkCompleted()
    {
        if (!_tasks.TrueForAll(static task => task.State == RunTaskState.Completed))
            return false;

        if (ScriptSteps == null || _nextScheduleStep >= ScriptSteps.Count)
            return true;
        var message = _tasks.Count == 0 ? "Scripted schedule contained unused steps, but no workers were forked."
            : "Scripted schedule contained unused steps after all workers completed.";
        throw CreateException(message, null);
    }

    /// <summary>
    /// Creates the timeout failure. While a worker runs, braid keeps every other worker parked before its start or at its probe,
    /// so a run that times out with a running and a parked worker may hang only because the running worker waits for the parked one,
    /// which real threads would not do.
    /// </summary>
    /// <param name="exception">The cancellation raised by the run timeout.</param>
    /// <returns>
    /// A <see cref="RunFailureOrigin.Scheduler" /> failure when a worker runs while others are parked; otherwise a <see cref="RunFailureOrigin.Timeout" /> failure.
    /// </returns>
    private RunException CreateTimeoutException(OperationCanceledException exception)
    {
        bool blockedOnParkedWorker;
        lock (_gate)
        {
            blockedOnParkedWorker = _tasks.Exists(static task => task.State is RunTaskState.Running)
                && _tasks.Exists(static task => task.State is RunTaskState.Waiting or RunTaskState.Held);
        }

        return blockedOnParkedWorker
            ? CreateException(ParkedTimeoutMessage, exception, RunFailureOrigin.Scheduler)
            : CreateException("braid run timed out.", exception, RunFailureOrigin.Timeout);
    }

    private string AppendReplayState(string message)
    {
        if (ScriptSteps == null)
            return message;

        var details = new List<string>
        {
            message,
            $"Next replay step: {_nextScheduleStep + 1} of {ScriptSteps.Count}",
        };

        if (_nextScheduleStep >= ScriptSteps.Count)
            return string.Join(Environment.NewLine, details);
        var step = ScriptSteps[_nextScheduleStep];
        details.Add($"Next replay operation: {FormatStepLocal(step)}");

        return string.Join(Environment.NewLine, details);

        static string FormatStepLocal(ReplayStep s)
        {
            return s.Kind is ReplayStepKind.Hit ? $"Hit {s.WorkerId} at {s.ProbeName}" : $"{s.Kind} {s.WorkerId} at {s.ProbeName}";
        }
    }

    private SchedulerDiagnostics BuildDiagnosticSnapshot()
    {
        var hasReplay = ScriptSteps?.Count > 0;

        ReplayStep? lastMatched = null;
        int? lastMatchedOneBased = null;
        if (hasReplay && _nextScheduleStep > 0)
        {
            lastMatched = ScriptSteps![_nextScheduleStep - 1];
            lastMatchedOneBased = _nextScheduleStep;
        }

        var waiting = SchedulerSearch.CollectProbeWaitDiagnostics(_tasks, RunTaskState.Waiting, ProbeWaitDiagnostic.NotStartedProbeName);
        var held = SchedulerSearch.CollectProbeWaitDiagnostics(_tasks, RunTaskState.Held, null);
        var running = SchedulerSearch.CollectProbeWaitDiagnostics(_tasks, RunTaskState.Running, ProbeWaitDiagnostic.StartProbeName);

        (int OneBasedIndex, ReplayStep Step)[] unused;
        if (hasReplay && _nextScheduleStep < ScriptSteps!.Count)
        {
            var remaining = ScriptSteps.Count - _nextScheduleStep;
            unused = new (int, ReplayStep)[remaining];
            for (var index = 0; index < remaining; index++)
            {
                var scheduleIndex = _nextScheduleStep + index;
                unused[index] = (scheduleIndex + 1, ScriptSteps[scheduleIndex]);
            }
        }
        else
        {
            unused = [];
        }

        return new SchedulerDiagnostics(hasReplay, lastMatched, lastMatchedOneBased, waiting, held, unused, running);
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
        RunTaskSlot.Current = null;

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

        RunTaskSlot.Current = braidTask;
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
                    braidTask.Exception = operationCanceled is TaskCanceledException ? new OperationCanceledException(operationCanceled.Message, operationCanceled)
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
            CompleteForkedOperation(braidTask);

            // Removed last: once a worker leaves the running list, it no longer touches anything that Dispose releases.
            lock (_gate)
                _runningWorkers.Remove(registration.ForkTask);
        }
    }

    private async Task RunJoinSchedulerLoopAsync(CancellationToken cancellationToken, CancellationToken linkedToken)
    {
        var context = new SchedulerJoinContext
        {
            Tasks = _tasks,
            NextScheduleStep = _nextScheduleStep,
            Script = _script,
            Random = _random,
            Trace = _trace,
            CreateException = CreateException,
        };

        while (true)
        {
            linkedToken.ThrowIfCancellationRequested();

            RunTask? nextTask;
            var advancedWithoutRelease = false;

            lock (_gate)
            {
                context.NextScheduleStep = _nextScheduleStep;

                nextTask = SchedulerSearch.SelectNextJoinTask(context, cancellationToken, ref advancedWithoutRelease);
                _nextScheduleStep = context.NextScheduleStep;

                switch (nextTask)
                {
                    case null when advancedWithoutRelease:
                        continue;
                    case null when AllJoinWorkCompleted():
                        return;
                    default:
                        break;
                }

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
            runningTasks = _runningWorkers.Snapshot();
            if (runningTasks.Length == 0)
                _runningWorkers.ClearAbandoned();
        }

        if (runningTasks.Length == 0)
            return;

        var all = Task.WhenAll(runningTasks);
        if (_shutdownCts.IsCancellationRequested)
        {
            var completed = await Task.WhenAny(all, Task.Delay(ShutdownDrainTimeout, TimeProvider.System, CancellationToken.None)).ConfigureAwait(false);
            if (completed == all)
                await all.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

            lock (_gate)
            {
                if (completed == all)
                    _runningWorkers.ClearAbandoned();
                else
                    _runningWorkers.RecordAbandoned(_tasks);
            }

            return;
        }

        await all.ConfigureAwait(false);
    }

    private static class SchedulerSearch
    {
        internal static ProbeWaitDiagnostic[] CollectProbeWaitDiagnostics(List<RunTask> tasks, RunTaskState state, string? probeNameBeforeFirstProbe)
        {
            var matches = new List<RunTask>();
            for (var index = 0; index < tasks.Count; index++)
            {
                var task = tasks[index];
                if (task.State == state && (task.LastProbeName != null || probeNameBeforeFirstProbe != null))
                    matches.Add(task);
            }

            if (matches.Count == 0)
                return [];

            matches.Sort(static (left, right) => left.Id.CompareTo(right.Id));
            var diagnostics = new ProbeWaitDiagnostic[matches.Count];
            for (var matchIndex = 0; matchIndex < matches.Count; matchIndex++)
            {
                var task = matches[matchIndex];
                diagnostics[matchIndex] = new ProbeWaitDiagnostic(task.WorkerId, task.LastProbeName ?? probeNameBeforeFirstProbe!);
            }

            return diagnostics;
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

        internal static Task InvokeForkOperationAsync(object? state)
        {
            if (state is not Func<Task> operation)
                throw new InvalidOperationException("Invalid fork operation.");

            var task = operation();
            return task ?? Task.FromException(new InvalidOperationException("Fork operation returned a null task."));
        }

        internal static RunTask? SelectNextJoinTask(SchedulerJoinContext context, CancellationToken cancellationToken, ref bool advancedWithoutRelease)
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
            return SelectNextTask(context, waitingTasks, hasRunningTasks, ref advancedWithoutRelease);
        }

        private static RunTask[] CollectWaitingTasksSortedById(List<RunTask> tasks)
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

        private static RunTask? FindHeldTask(List<RunTask> tasks, string workerId, string probeName)
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

        private static RunTask? FindSameWorkerBlockedTask(List<RunTask> tasks, string workerId)
        {
            for (var index = 0; index < tasks.Count; index++)
            {
                var task = tasks[index];
                var equals = string.Equals(task.WorkerId, workerId, StringComparison.Ordinal);
                if (equals && (task.State == RunTaskState.Waiting || task.State == RunTaskState.Held) && task.LastProbeName != null)
                    return task;
            }

            return null;
        }

        private static RunTask? FindWaitingTask(RunTask[] waitingTasks, string workerId, string probeName)
        {
            for (var index = 0; index < waitingTasks.Length; index++)
            {
                var task = waitingTasks[index];
                if (string.Equals(task.WorkerId, workerId, StringComparison.Ordinal) && string.Equals(task.LastProbeName, probeName, StringComparison.Ordinal))
                    return task;
            }

            return null;
        }

        private static string BuildStepMismatchMessage(int stepIndex, string action, in ReplayStep expectedStep, RunTask? sameWorkerBlockedTask)
        {
            var oneBasedIndex = stepIndex + 1;
            return sameWorkerBlockedTask?.LastProbeName == null
                ? $"Scripted schedule step {oneBasedIndex} could not be satisfied: {action} {expectedStep.WorkerId} at {expectedStep.ProbeName}."
                : $"Scripted schedule step {oneBasedIndex} could not be satisfied: {action} {expectedStep.WorkerId} at {expectedStep.ProbeName}; actual probe is {sameWorkerBlockedTask.LastProbeName}.";
        }

        private static RunTask? SelectStartupTask(RunTask[] waitingTasks)
        {
            for (var index = 0; index < waitingTasks.Length; index++)
            {
                var task = waitingTasks[index];
                if (task.LastProbeName == null)
                    return task;
            }

            return null;
        }

        private static RunTask? SelectArriveStep(
            SchedulerJoinContext context,
            in ReplayStep step,
            RunTask? waitingTask,
            RunTask? sameWorkerBlockedTask,
            bool hasRunningTasks,
            ref bool advancedWithoutRelease)
        {
            if (waitingTask == null)
            {
                var message = BuildStepMismatchMessage(context.NextScheduleStep, "arrive", in step, sameWorkerBlockedTask);
                return hasRunningTasks ? null : throw context.CreateException(message, null, RunFailureOrigin.Scheduler);
            }

            waitingTask.State = RunTaskState.Held;
            context.NextScheduleStep++;
            advancedWithoutRelease = true;
            context.Trace.Add($"{waitingTask.WorkerId} arrival observed at {waitingTask.LastProbeName} (held)");
            return null;
        }

        private static RunTask? SelectHitStep(
            SchedulerJoinContext context,
            in ReplayStep step,
            RunTask? waitingTask,
            RunTask? heldTask,
            RunTask? sameWorkerBlockedTask,
            bool hasRunningTasks)
        {
            var releasableTask = heldTask ?? waitingTask;
            if (releasableTask == null)
            {
                return hasRunningTasks ? null : throw context.CreateException(
                    BuildStepMismatchMessage(context.NextScheduleStep, "hit", in step, sameWorkerBlockedTask),
                    null,
                    RunFailureOrigin.Scheduler);
            }

            context.NextScheduleStep++;
            return releasableTask;
        }

        private static RunTask? SelectNextTask(SchedulerJoinContext context, RunTask[] waitingTasks, bool hasRunningTasks, ref bool advancedWithoutRelease)
        {
            var startupTask = SelectStartupTask(waitingTasks);
            return startupTask ?? SelectNonStartupTask(context, waitingTasks, hasRunningTasks, ref advancedWithoutRelease);
        }

        private static RunTask? SelectNonStartupTask(SchedulerJoinContext context, RunTask[] waitingTasks, bool hasRunningTasks, ref bool advancedWithoutRelease)
        {
            return context.Steps == null
                ? SelectRandomWaitingTask(waitingTasks, context)
                : SelectScriptedTask(context, waitingTasks, hasRunningTasks, ref advancedWithoutRelease);
        }

        private static RunTask? SelectRandomWaitingTask(RunTask[] waitingTasks, SchedulerJoinContext context)
            => waitingTasks.Length == 0 ? null : waitingTasks[context.Random.NextInt32(waitingTasks.Length)];

        private static RunTask? SelectScriptedTask(SchedulerJoinContext context, RunTask[] waitingTasks, bool hasRunningTasks, ref bool advancedWithoutRelease)
        {
            return context.NextScheduleStep < context.Steps!.Count
                ? SelectScheduledTask(context, waitingTasks, hasRunningTasks, ref advancedWithoutRelease)
                : SelectExhaustedScriptTask(context, waitingTasks, hasRunningTasks, ref advancedWithoutRelease);
        }

        private static RunTask? SelectExhaustedScriptTask(SchedulerJoinContext context, RunTask[] waitingTasks, bool hasRunningTasks, ref bool advancedWithoutRelease)
        {
            return context.Script!.CompletesInForkOrder
                ? SelectForkOrderCompletionTask(context, waitingTasks, hasRunningTasks, ref advancedWithoutRelease)
                : throw context.CreateException(ScriptExhaustedMessage, null, RunFailureOrigin.Scheduler);
        }

        /// <summary>
        /// Releases the first waiting worker in fork order once no worker is running, so the choice depends only on which workers are parked.
        /// The choice is appended to the script as a hit step, so the reported schedule replays the whole run.
        /// </summary>
        /// <param name="context">The join context.</param>
        /// <param name="waitingTasks">The waiting workers, sorted by fork order.</param>
        /// <param name="hasRunningTasks">Whether a worker is still running.</param>
        /// <param name="advancedWithoutRelease">Set when the step advanced without releasing a worker.</param>
        /// <returns>The worker to release, or <see langword="null"/> to wait for a state change.</returns>
        /// <exception cref="RunException">Only held workers remain, so none can be released.</exception>
        private static RunTask? SelectForkOrderCompletionTask(SchedulerJoinContext context, RunTask[] waitingTasks, bool hasRunningTasks, ref bool advancedWithoutRelease)
        {
            if (hasRunningTasks)
                return null;

            // Fail now instead of waiting for the run timeout.
            if (waitingTasks.Length == 0)
                throw context.CreateException(ScriptExhaustedMessage, null, RunFailureOrigin.Scheduler);

            var task = waitingTasks[0];
            context.Script!.AppendCompletionStep(ReplayStep.Hit(task.WorkerId, task.LastProbeName!));
            return SelectScheduledTask(context, waitingTasks, hasRunningTasks, ref advancedWithoutRelease);
        }

        private static RunTask? SelectReleaseStep(SchedulerJoinContext context, in ReplayStep step, RunTask? heldTask, RunTask? sameWorkerBlockedTask, bool hasRunningTasks)
        {
            if (heldTask == null)
            {
                var message = BuildStepMismatchMessage(context.NextScheduleStep, "release held", in step, sameWorkerBlockedTask);
                return hasRunningTasks ? null : throw context.CreateException(message, null, RunFailureOrigin.Scheduler);
            }

            context.NextScheduleStep++;
            return heldTask;
        }

        private static RunTask? SelectScheduledTask(SchedulerJoinContext context, RunTask[] waitingTasks, bool hasRunningTasks, ref bool advancedWithoutRelease)
        {
            var step = context.Steps![context.NextScheduleStep];
            var waitingTask = FindWaitingTask(waitingTasks, step.WorkerId, step.ProbeName);
            var heldTask = FindHeldTask(context.Tasks, step.WorkerId, step.ProbeName);
            var sameWorkerBlockedTask = FindSameWorkerBlockedTask(context.Tasks, step.WorkerId);

            return step.Kind switch
            {
                ReplayStepKind.Hit => SelectHitStep(context, in step, waitingTask, heldTask, sameWorkerBlockedTask, hasRunningTasks),
                ReplayStepKind.Arrive when heldTask != null => throw context.CreateException(
                    $"Scripted schedule step {context.NextScheduleStep + 1} could not be satisfied: duplicate Arrive for held {step.WorkerId} at {step.ProbeName}.",
                    null,
                    RunFailureOrigin.Scheduler),
                ReplayStepKind.Arrive => SelectArriveStep(context, in step, waitingTask, sameWorkerBlockedTask, hasRunningTasks, ref advancedWithoutRelease),
                ReplayStepKind.Release => SelectReleaseStep(context, in step, heldTask, sameWorkerBlockedTask, hasRunningTasks),
                _ => throw context.CreateException($"Scripted schedule step {context.NextScheduleStep + 1} has unknown step kind {step.Kind}.", null, RunFailureOrigin.Scheduler),
            };
        }
    }

    private sealed class ForkOperationRegistration
    {
        internal Task ForkTask { get; set; } = Task.CompletedTask;
    }
}
