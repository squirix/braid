using System.Runtime.ExceptionServices;

namespace Braid;

/// <summary>Runs deterministic concurrency tests by controlling logical workers at explicit async probe points.</summary>
public static class Runner
{
    private const string CallbackTimeoutMessage =
        "braid run timed out before the callback called JoinAsync. Forked workers start only when the run joins, "
        + "so a callback that waits for a forked worker before JoinAsync never continues. The callback was abandoned and may keep running.";

    private const string NotRepeatableMessage =
        "The test did not repeat under the same schedule: a step that an earlier run took could not be taken again. "
        + "Exploration needs a test that takes the same steps whenever its workers are released in the same order.";

    /// <summary>
    /// Explores the schedules of the supplied workers and probe points within the configured bounds, stopping at the first test failure.
    /// The search is depth-first over the choices of real runs: each run replays the steps of an earlier run up to one choice, releases another
    /// waiting worker there, and from then on releases the worker that has waited longest. A schedule therefore fits the test also when the order of the workers
    /// changes which probes a worker hits. The callback must not return null.
    /// </summary>
    /// <remarks>
    /// Every run starts its callback on the thread pool, without the synchronization context or task scheduler of the caller.
    /// The test must take the same steps whenever its workers are released in the same order.
    /// </remarks>
    /// <param name="configure">Configures exploration bounds and seed.</param>
    /// <param name="test">The exploration callback.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="Task" /> that completes when exploration finishes without finding a failure.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure" /> or <paramref name="test" /> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Configured bounds are invalid.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> was canceled.</exception>
    /// <exception cref="RunException">
    /// A test failure, a timeout or an API misuse error was found under a schedule; the test did not take the same steps under the same schedule;
    /// every schedule that ran hung on a worker waiting for a parked one; or the callback returned a null task
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
        var search = new ExploreSearch(options.MaxStepsPerSchedule);
        RunException? firstHang = null;
        var anyRunCompleted = false;
        var hasNext = true;

        for (var run = 0; hasNext && run < options.MaxSchedules; run++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var runOptions = new RunOptions
            {
                Iterations = 1,
                Seed = options.Seed,
                Timeout = options.Timeout,
                ExploredPrefix = search.Prefix,
            };

            try
            {
                await RunAsync(callback.InvokeAsync, runOptions, cancellationToken).ConfigureAwait(false);
                anyRunCompleted = true;
            }
            catch (RunException ex) when (ex.SkippedByExploration)
            {
                // A prefix repeats steps that an earlier run took, so the run takes all of them unless the test changed between the runs.
                if (!IsHang(ex) || ex.SchedulerDiagnostics is { UnusedReplaySteps.Count: > 0 })
                    throw new RunException(NotRepeatableMessage, ex.Context, ex);

                // The run hung on a worker that waits for a parked one. Its choices end at the hang, so the search moves on from there
                // and runs no other schedule that begins the same way.
                System.Diagnostics.Trace.TraceInformation($"Braid: skipping a schedule that hung ({ex}).");
                firstHang ??= ex;
            }

            hasNext = search.MoveNext(callback.TakeExploredChoices());
        }

        // No schedule ran to its end, so nothing was checked: passing would hide that.
        if (!anyRunCompleted && firstHang != null)
            ExceptionDispatchInfo.Throw(firstHang);
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

    /// <summary>
    /// Decides whether a failure that exploration skips is a hang on a worker that waits for a parked one.
    /// Among the failures that exploration skips, only that timeout carries the cancellation of the run timeout.
    /// </summary>
    /// <param name="ex">The failure that exploration skips.</param>
    /// <returns><see langword="true" /> for a hang; <see langword="false" /> when the steps of the run did not fit the test.</returns>
    private static bool IsHang(RunException ex) => ex.InnerException is OperationCanceledException;

    private sealed class ExploreCallback
    {
        private readonly Func<ExploreContext, Task> _callback;
        private RunContext? _context;

        internal ExploreCallback(Func<ExploreContext, Task> callback)
        {
            _callback = callback;
        }

        internal Task InvokeAsync(RunContext context)
        {
            _context = context;
            return _callback(new ExploreContext(context));
        }

        /// <summary>Takes the choices that the last run made after the steps it replayed.</summary>
        /// <returns>The choices, or an empty list when the run stopped before its callback started.</returns>
        internal IReadOnlyList<ReplayStep[]> TakeExploredChoices()
        {
            var choices = _context?.ExploredChoices ?? [];
            _context = null;
            return choices;
        }
    }
}
