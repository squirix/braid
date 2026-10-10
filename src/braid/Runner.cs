using Braid.Attributes;

namespace Braid;

/// <summary>Runs deterministic concurrency tests by controlling logical workers at explicit async probe points.</summary>
public static class Runner
{
    private const string CallbackTimeoutMessage =
        "braid run timed out before the callback called JoinAsync. Forked workers start only when the run joins, "
        + "so a callback that waits for a forked worker before JoinAsync never continues. The callback was abandoned and may keep running.";

    /// <summary>
    /// Explores bounded replay schedules for the supplied workers and probe points, stopping at the first test failure.
    /// Discovery uses one random run to learn per-worker probe sequences, then tries generated schedules of start and hit steps up to the configured bounds.
    /// The callback must not return null.
    /// </summary>
    /// <remarks>Every run, discovery or generated, starts its callback on the thread pool, without the synchronization context or task scheduler of the caller.</remarks>
    /// <param name="configure">Configures exploration bounds and seed.</param>
    /// <param name="test">The exploration callback.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="Task" /> that completes when exploration finishes without finding a failure.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure" /> or <paramref name="test" /> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Configured bounds are invalid.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> was canceled.</exception>
    /// <exception cref="RunException">
    /// A test failure, a timeout or an API misuse error was found under a replay schedule or during discovery, or the callback returned a null task
    /// (reported with <see cref="RunFailureOrigin.UserTest" /> and an inner <see cref="InvalidOperationException" />).
    /// </exception>
    public static Task ExploreAsync(Action<ExploreOptionsBuilder> configure, Func<ExploreContext, Task> test, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentNullException.ThrowIfNull(test);

        var builder = new ExploreOptionsBuilder();
        configure(builder);
        return ExploreAsync(builder.Build(), test, cancellationToken);
    }

    /// <inheritdoc cref="ExploreAsync(Action{ExploreOptionsBuilder}, Func{ExploreContext, Task}, CancellationToken)" />
    public static Task ExploreAsync(in ExploreOptions options, Func<ExploreContext, Task> test, CancellationToken cancellationToken) =>
        ExploreAsyncCoreAsync(options, test, cancellationToken);

    /// <summary>
    /// Runs the supplied test callback across one or more deterministic scheduling iterations.
    /// After the callback task completes successfully, forked workers are joined automatically; an explicit
    /// <see cref="RunContext.JoinAsync(System.Threading.CancellationToken)" /> at the end of the callback is optional.
    /// The callback must not return null.
    /// </summary>
    /// <remarks>
    /// The callback of every iteration starts on the thread pool, without the synchronization context or task scheduler of the caller,
    /// so the method can return before the callback starts; a token canceled in between can cancel the run before the callback is invoked.
    /// </remarks>
    /// <param name="test">The test callback to execute.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="Task" /> that completes when all iterations pass.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="test" /> is null.</exception>
    /// <exception cref="InvalidOperationException">A braid run is already active.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> was canceled.</exception>
    /// <exception cref="RunException">
    /// A forked worker failed, the run timed out, scheduling could not satisfy the replay script, or the callback returned a null task
    /// (reported with <see cref="RunFailureOrigin.UserTest" /> and an inner <see cref="InvalidOperationException" />).
    /// </exception>
    public static Task RunAsync(Func<RunContext, Task> test, CancellationToken cancellationToken) => RunAsync(test, null, cancellationToken);

    /// <summary>
    /// Runs the supplied test callback across one or more deterministic scheduling iterations.
    /// After the callback task completes successfully, forked workers are joined automatically; an explicit
    /// <see cref="RunContext.JoinAsync(System.Threading.CancellationToken)" /> at the end of the callback is optional.
    /// The callback must not return null.
    /// </summary>
    /// <remarks>
    /// The callback of every iteration starts on the thread pool, without the synchronization context or task scheduler of the caller,
    /// so the method can return before the callback starts; a token canceled in between can cancel the run before the callback is invoked.
    /// </remarks>
    /// <param name="test">The test callback to execute.</param>
    /// <param name="options">The run options.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="Task" /> that completes when all iterations pass.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="test" /> is null.</exception>
    /// <exception cref="InvalidOperationException">A braid run is already active.</exception>
    /// <exception cref="ArgumentException"><paramref name="options" /> failed validation.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> was canceled.</exception>
    /// <exception cref="RunException">
    /// A forked worker failed, the run timed out, scheduling could not satisfy the replay script, or the callback returned a null task
    /// (reported with <see cref="RunFailureOrigin.UserTest" /> and an inner <see cref="InvalidOperationException" />).
    /// </exception>
    public static Task RunAsync(Func<RunContext, Task> test, RunOptions? options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(test);

        if (RunScope.CurrentScheduler != null)
            throw new InvalidOperationException("Nested braid runs are not supported.");

        cancellationToken.ThrowIfCancellationRequested();

        var resolvedOptions = options ?? RunOptions.Default;
        resolvedOptions.Validate();

        return RunAsyncCoreAsync(test, resolvedOptions, cancellationToken);
    }

    private static async Task ExploreAsyncCoreAsync(ExploreOptions options, Func<ExploreContext, Task> test, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(test);
        options.Validate();

        var callback = new ExploreCallback(test);
        var discoveryOptions = new RunOptions
        {
            Iterations = 1,
            Seed = options.Seed,
            Timeout = options.Timeout,
            IsDiscoveryRun = true,
        };

        RunException? discoveryFailure = null;

        try
        {
            await RunAsync(callback.RunDiscoveryAsync, discoveryOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (RunException ex)
        {
            discoveryFailure = ex;
        }

        var workerProbeSequences = callback.DiscoveryContext?.WorkerProbeSequences ?? [];

        // When sequences were discovered, a target failure is deferred: generated schedules may reproduce it with a replay token.
        // If none does, the discovery failure is surfaced, so exploration never passes after a target failure was observed.
        if (workerProbeSequences.Count > 0)
            await ExploreGeneratedSchedulesAsync(options, callback, workerProbeSequences, cancellationToken).ConfigureAwait(false);

        if (discoveryFailure != null && IsExplorationTargetFailure(discoveryFailure))
            throw discoveryFailure;
    }

    private static async Task RunAsyncCoreAsync(Func<RunContext, Task> test, RunOptions resolvedOptions, CancellationToken cancellationToken)
    {
        // Yields to the thread pool once, without the caller's synchronization context or task scheduler,
        // so the callback of the first iteration starts like the later ones and not inline in the caller.
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);

        var baseSeed = resolvedOptions.Seed ?? Environment.TickCount;

        for (var iteration = 0; iteration < resolvedOptions.Iterations; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var seed = unchecked(baseSeed + iteration);
            using var scheduler = new Scheduler(seed, iteration, resolvedOptions);
            var context = new RunContext(scheduler);

            using var scope = RunScope.Enter(scheduler);

            try
            {
                await RunCallbackAsync(scheduler, test, context, cancellationToken).ConfigureAwait(false);
                await context.JoinAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (RunException ex)
            {
                await scheduler.StopAsync().ConfigureAwait(false);
                scheduler.ReportAbandonedWorkers(ex);
                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await scheduler.StopAsync().ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                await scheduler.StopAsync().ConfigureAwait(false);
                var failure = scheduler.CreateException("braid run failed.", ex, RunFailureOrigin.UserTest);
                scheduler.ReportAbandonedWorkers(failure);
                throw failure;
            }
            finally
            {
                context.Complete();
            }
        }
    }

    /// <summary>
    /// Runs and awaits the test callback. Forked workers start only when the run joins, so a callback that waits for a forked worker before it joins
    /// never continues: until the callback joins, the run timeout and <paramref name="cancellationToken" /> end the wait.
    /// After the callback joins, the join applies the run timeout to itself only. A callback that blocks synchronously before its first await is not bounded.
    /// </summary>
    /// <param name="scheduler">The scheduler of the run.</param>
    /// <param name="test">The test callback.</param>
    /// <param name="context">The context passed to <paramref name="test" />.</param>
    /// <param name="cancellationToken">The cancellation token of the run.</param>
    /// <returns>A task that completes when the callback completes.</returns>
    /// <exception cref="InvalidOperationException"><paramref name="test" /> returned a null task.</exception>
    /// <exception cref="RunException">The run timed out before the callback joined.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> was canceled before the callback joined.</exception>
    private static async Task RunCallbackAsync(Scheduler scheduler, Func<RunContext, Task> test, RunContext context, CancellationToken cancellationToken)
    {
        var callbackTask = test(context) ?? throw new InvalidOperationException("Braid run callback returned a null task.");
        using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, scheduler.TimeoutToken))
            await callbackTask.WaitAsync(linkedCts.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        if (!callbackTask.IsCompleted && !scheduler.HasJoined)
        {
            // The callback is left behind; observe a later fault so it is not reported as unobserved.
            _ = callbackTask.ContinueWith(static task => _ = task.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            cancellationToken.ThrowIfCancellationRequested();
            throw scheduler.CreateException(CallbackTimeoutMessage, null, RunFailureOrigin.Timeout);
        }

        await callbackTask.ConfigureAwait(false);
    }

    private static async Task ExploreGeneratedSchedulesAsync(
        ExploreOptions options,
        ExploreCallback callback,
        IReadOnlyList<WorkerProbes> workerProbeSequences,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ReplayStep> hungSchedule = [];
        var hungPrefixLength = 0;

        foreach (var steps in ExploreScheduleEnumerator.EnumerateSchedules(workerProbeSequences, options.MaxSchedules, options.MaxStepsPerSchedule))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // A schedule that begins like one that hung hangs the same way; running it would only wait for the run timeout again.
            if (StartsWithPrefix(steps, hungSchedule, hungPrefixLength))
                continue;

            hungPrefixLength = 0;
            var schedule = ReplaySchedule.Replay(steps);
            try
            {
                await RunScheduledExploreAttemptAsync(in options, callback, schedule, cancellationToken).ConfigureAwait(false);
            }
            catch (RunException ex) when (IsExplorationTargetFailure(ex))
            {
                throw;
            }
            catch (RunException ex)
            {
                System.Diagnostics.Trace.TraceInformation($"Braid: skipping non-target schedule ({ex}).");
                hungSchedule = steps;
                hungPrefixLength = GetHungPrefixLength(ex, steps.Count);
            }
        }
    }

    /// <summary>
    /// Gets the number of leading steps after which a skipped schedule hung on a worker that waits for a parked one.
    /// Among the failures that exploration skips, only that timeout carries the cancellation of the run timeout.
    /// </summary>
    /// <param name="ex">The failure that exploration skipped.</param>
    /// <param name="stepCount">The number of steps of the generated schedule.</param>
    /// <returns>The length of the prefix that leads to the hang, or zero when the schedule was skipped for another reason.</returns>
    private static int GetHungPrefixLength(RunException ex, int stepCount) =>
        ex.InnerException is OperationCanceledException ? Math.Min(ex.SchedulerDiagnostics?.LastMatchedReplayStepOneBased ?? 0, stepCount) : 0;

    private static bool StartsWithPrefix(IReadOnlyList<ReplayStep> steps, IReadOnlyList<ReplayStep> prefixSource, int prefixLength)
    {
        if (prefixLength == 0 || steps.Count < prefixLength)
            return false;

        for (var index = 0; index < prefixLength; index++)
        {
            if (steps[index] != prefixSource[index])
                return false;
        }

        return true;
    }

    /// <summary>
    /// Decides whether a failure stops exploration. Only failures that come from the schedule itself are skipped: a schedule that does not fit
    /// the run, or a hang caused by braid keeping a worker parked. Test failures, timeouts and API misuse errors stop exploration.
    /// </summary>
    /// <param name="ex">The failure.</param>
    /// <returns><see langword="true" /> if the failure stops exploration; otherwise <see langword="false" />.</returns>
    private static bool IsExplorationTargetFailure(RunException ex) => !ex.SkippedByExploration;

    private static Task RunScheduledExploreAttemptAsync(in ExploreOptions options, ExploreCallback callback, ReplaySchedule schedule, CancellationToken cancellationToken)
    {
        var runOptions = new RunOptions
        {
            Iterations = 1,
            Seed = options.Seed,
            Schedule = schedule,
            Timeout = options.Timeout,
            CompletesScheduleInForkOrder = true,
        };

        return RunAsync(callback.RunReplayAsync, runOptions, cancellationToken);
    }

    /// <summary>Enumerates the interleavings of the workers' steps: a start step per worker, then one hit step per probe it hit in the discovery run.</summary>
    private static class ExploreScheduleEnumerator
    {
        internal static IEnumerable<IReadOnlyList<ReplayStep>> EnumerateSchedules(IReadOnlyList<WorkerProbes> workers, int maxSchedules, int maxHitsPerSchedule)
        {
            ArgumentNullException.ThrowIfNull(workers);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSchedules);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxHitsPerSchedule);

            return EnumerateSchedulesCore(workers, maxSchedules, maxHitsPerSchedule);
        }

        private static bool AllWorkersCompleted(IReadOnlyList<WorkerProbes> workers, int[] progress)
        {
            for (var index = 0; index < workers.Count; index++)
            {
                if (progress[index] <= workers[index].ProbeNames.Count)
                    return false;
            }

            return true;
        }

        private static int[] CloneProgress(int[] progress)
        {
            var copy = new int[progress.Length];
            for (var index = 0; index < progress.Length; index++)
                copy[index] = progress[index];

            return copy;
        }

        private static ReplayStep[] CopySteps(List<ReplayStep> steps)
        {
            var copy = new ReplayStep[steps.Count];
            for (var index = 0; index < steps.Count; index++)
                copy[index] = steps[index];

            return copy;
        }

        /// <summary>Orders the workers by id for hit steps, the order the enumeration had before start steps existed.</summary>
        /// <param name="workers">The workers in fork order.</param>
        /// <returns>The indexes of the workers, sorted by worker id.</returns>
        private static int[] CreateHitOrder(IReadOnlyList<WorkerProbes> workers)
        {
            var order = new int[workers.Count];
            for (var i = 0; i < order.Length; i++)
            {
                var j = i;
                while (j > 0 && string.CompareOrdinal(workers[order[j - 1]].WorkerId, workers[i].WorkerId) > 0)
                {
                    order[j] = order[j - 1];
                    j--;
                }

                order[j] = i;
            }

            return order;
        }

        private static IEnumerable<IReadOnlyList<ReplayStep>> EnumerateSchedulesCore(IReadOnlyList<WorkerProbes> workers, int maxSchedules, int maxHits)
        {
            if (workers.Count == 0)
                yield break;

            var hitOrder = CreateHitOrder(workers);
            var yielded = 0;
            var stack = new Stack<SearchFrame>();
            stack.Push(new SearchFrame(new int[workers.Count], [], 0, 0));

            while (stack.Count > 0 && yielded < maxSchedules)
            {
                var frame = stack.Pop();

                // A schedule cut at the hit limit is completed in fork order by the run, like one that ends before the test does.
                if (frame.Hits == maxHits || AllWorkersCompleted(workers, frame.Progress))
                {
                    yielded++;
                    yield return CopySteps(frame.Steps);
                    continue;
                }

                ScheduleNextWorker(workers, hitOrder, in frame, stack);
            }
        }

        private static void PushContinuation(Stack<SearchFrame> stack, in SearchFrame frame, int workerIndex, in ReplayStep step, int nextCandidate)
        {
            var nextProgress = CloneProgress(frame.Progress);
            nextProgress[workerIndex]++;
            var nextSteps = new List<ReplayStep>(frame.Steps) { step };

            stack.Push(frame with { NextCandidate = nextCandidate });
            stack.Push(new SearchFrame(nextProgress, nextSteps, step.Kind is ReplayStepKind.Hit ? frame.Hits + 1 : frame.Hits, 0));
        }

        /// <summary>
        /// Pushes the next untried continuation of a frame. Starts come first, in fork order, so the first schedules start every worker before any hit,
        /// as every run did before start steps existed; later schedules move the starts between the hits.
        /// </summary>
        /// <param name="workers">The workers in fork order.</param>
        /// <param name="hitOrder">The indexes of the workers, sorted by worker id.</param>
        /// <param name="frame">The frame to continue.</param>
        /// <param name="stack">The search stack.</param>
        private static void ScheduleNextWorker(IReadOnlyList<WorkerProbes> workers, int[] hitOrder, in SearchFrame frame, Stack<SearchFrame> stack)
        {
            var candidate = 0;
            for (var workerIndex = 0; workerIndex < workers.Count; workerIndex++)
            {
                if (frame.Progress[workerIndex] != 0)
                    continue;

                candidate++;
                if (candidate <= frame.NextCandidate)
                    continue;

                PushContinuation(stack, in frame, workerIndex, ReplayStep.Start(workers[workerIndex].WorkerId), candidate);
                return;
            }

            for (var position = 0; position < hitOrder.Length; position++)
            {
                var workerIndex = hitOrder[position];
                var progress = frame.Progress[workerIndex];
                if (progress == 0 || progress > workers[workerIndex].ProbeNames.Count)
                    continue;

                candidate++;
                if (candidate <= frame.NextCandidate)
                    continue;

                PushContinuation(stack, in frame, workerIndex, ReplayStep.Hit(workers[workerIndex].WorkerId, workers[workerIndex].ProbeNames[progress - 1]), candidate);
                return;
            }
        }

        [Mutable]
        private readonly record struct SearchFrame(int[] Progress, List<ReplayStep> Steps, int Hits, int NextCandidate);
    }

    private sealed class ExploreCallback
    {
        private readonly Func<ExploreContext, Task> _callback;

        internal ExploreCallback(Func<ExploreContext, Task> callback)
        {
            _callback = callback;
        }

        internal RunContext? DiscoveryContext { get; private set; }

        internal Task RunDiscoveryAsync(RunContext context)
        {
            DiscoveryContext = context;
            return _callback(new ExploreContext(context));
        }

        internal Task RunReplayAsync(RunContext context) => _callback(new ExploreContext(context));
    }
}
