using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Xunit;

namespace Braid.Tests;

/// <summary>Provides shared test helpers.</summary>
public abstract class TestBase
{
    /// <summary>Gets the xUnit cancellation token for the current test.</summary>
    protected static CancellationToken DefaultCancellationToken => TestContext.Current.CancellationToken;

    /// <summary>Starts and waits for a task to complete before a watchdog timeout without blocking a thread.</summary>
    /// <param name="startTask">Starts the task to wait for.</param>
    /// <param name="failureMessage">The message used when the watchdog wins.</param>
    /// <param name="watchdogTimeout">The watchdog duration. Defaults to two seconds.</param>
    /// <param name="prefixWatchdogMessage">Whether to prefix the failure message with a standard braid watchdog sentence.</param>
    protected static Task AssertCompletesBeforeWatchdogAsync(Func<Task> startTask, string failureMessage, TimeSpan watchdogTimeout = default, bool prefixWatchdogMessage = true)
    {
        ArgumentNullException.ThrowIfNull(startTask);
        var effectiveTimeout = watchdogTimeout == TimeSpan.Zero ? TimeSpan.FromSeconds(2) : watchdogTimeout;
        return BraidTestInternals.RunWatchdogAsync(startTask, failureMessage, effectiveTimeout, prefixWatchdogMessage);
    }

    /// <summary>Waits for an already-started task to complete before a watchdog timeout without blocking a thread.</summary>
    /// <param name="startedTask">The task that is already running.</param>
    /// <param name="failureMessage">The message used when the watchdog wins.</param>
    /// <param name="watchdogTimeout">The watchdog duration.</param>
    /// <param name="prefixWatchdogMessage">Whether to prefix the failure message with a standard braid watchdog sentence.</param>
    protected static async Task AssertCompletesBeforeWatchdogAsync(Task startedTask, string failureMessage, TimeSpan watchdogTimeout, bool prefixWatchdogMessage = true)
    {
        ArgumentNullException.ThrowIfNull(startedTask);
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = startedTask.ContinueWith(
            static (_, state) =>
            {
                if (state is TaskCompletionSource<bool> source)
                    source.SetResult(true);
            },
            completed,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        var watchdog = Task.Delay(watchdogTimeout, TimeProvider.System, DefaultCancellationToken);
        if (await Task.WhenAny(completed.Task, watchdog).ConfigureAwait(false) != completed.Task)
            Assert.Fail(prefixWatchdogMessage ? $"Braid run did not complete before watchdog timeout. {failureMessage}" : failureMessage);

        _ = await completed.Task.ConfigureAwait(false);
        BraidTestInternals.RethrowIfFaultedOrCanceled(startedTask);
    }

    /// <summary>Asserts a concurrent probe race run fails with the expected braid error, or silently serializes without failure.</summary>
    /// <param name="startRun">Starts the braid run to observe.</param>
    /// <param name="expectForkFailureMessage">Whether to assert the fork wrapper failure message.</param>
    protected static async Task AssertConcurrentProbeRaceToleratesAsync(Func<Task> startRun, bool expectForkFailureMessage = false)
    {
        ArgumentNullException.ThrowIfNull(startRun);
        var runTask = startRun();
        try
        {
            await runTask;
        }
        catch (RunException exception)
        {
            if (expectForkFailureMessage)
                Assert.Contains("A forked operation failed.", exception.Message, StringComparison.Ordinal);

            Assert.Contains("Concurrent probe hit on the same worker is not supported.", exception.ToString(), StringComparison.Ordinal);
        }
    }

    /// <summary>Asserts a concurrent probe race always fails with the expected braid error (no silent serialization).</summary>
    /// <param name="startRun">Starts the braid run to observe.</param>
    protected static async Task AssertConcurrentProbeRaceMustFailAsync(Func<Task> startRun)
    {
        ArgumentNullException.ThrowIfNull(startRun);
        var runTask = startRun();
        try
        {
            await runTask;
            Assert.Fail("Expected RunException for concurrent probe hit on the same worker.");
        }
        catch (RunException exception)
        {
            Assert.Contains("Concurrent probe hit on the same worker is not supported.", exception.ToString(), StringComparison.Ordinal);
        }
    }

    /// <summary>Asserts schedule text parsing does not throw.</summary>
    /// <param name="text">The schedule text to parse.</param>
    protected static void AssertTryParseDoesNotThrow(string? text)
    {
        var ex = Record.Exception(() => ReplaySchedule.TryParse(text, out _, out _));
        Assert.Null(ex);
    }

    /// <summary>Asserts a probe invoked outside an active run is a no-op that completes immediately.</summary>
    protected static async Task AssertProbeIsNoOpOutsideRunAsync()
    {
        var probe = Probe.HitAsync("outside-run", DefaultCancellationToken);
        Assert.True(probe.IsCompletedSuccessfully);
        await probe;
    }

    /// <summary>Forks a worker that hits ready and appends a worker label to the order list.</summary>
    /// <param name="context">The braid run context.</param>
    /// <param name="order">The list that receives the worker label.</param>
    /// <param name="workerLabel">The label to append after the ready probe.</param>
    protected static void ForkHitReadyAddWorker(RunContext context, IList<string> order, string workerLabel)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(order);
        context.Fork(async () =>
        {
            await Probe.HitAsync("ready", DefaultCancellationToken);
            order.Add(workerLabel);
        });
    }

    /// <summary>Forks a worker that hits ready and increments the completion counter.</summary>
    /// <param name="context">The braid run context.</param>
    /// <param name="completed">The shared completion counter.</param>
    protected static void ForkHitReadyAndIncrement(RunContext context, CompletionCounter completed)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(completed);
        context.Fork(async () =>
        {
            await Probe.HitAsync("ready", DefaultCancellationToken);
            _ = completed.Increment();
        });
    }

    /// <summary>Forks a worker that hits ready and enqueues its worker index.</summary>
    /// <param name="context">The braid run context.</param>
    /// <param name="workerIndex">The worker index to record.</param>
    /// <param name="releaseOrder">The queue that receives release order entries.</param>
    protected static void ForkHitReadyForWorker(RunContext context, int workerIndex, ConcurrentQueue<string> releaseOrder)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(releaseOrder);
        context.Fork(async () =>
        {
            await Probe.HitAsync("ready", DefaultCancellationToken);
            releaseOrder.Enqueue($"worker-{workerIndex}");
        });
    }

    /// <summary>Forks a worker that hits ready and records its name under a lock.</summary>
    /// <param name="context">The braid run context.</param>
    /// <param name="workerName">The worker name to record.</param>
    /// <param name="releases">The list that receives worker names.</param>
    /// <param name="gate">The lock protecting <paramref name="releases" />.</param>
    protected static void ForkHitReadyRecordWorker(RunContext context, string workerName, IList<string> releases, Lock gate)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(releases);
        context.Fork(async () =>
        {
            await Probe.HitAsync("ready", DefaultCancellationToken);
            lock (gate)
                releases.Add(workerName);
        });
    }

    /// <summary>Forks a probe-free worker that increments the completion counter.</summary>
    /// <param name="context">The braid run context.</param>
    /// <param name="completed">The shared completion counter.</param>
    protected static void ForkIncrementCompleted(RunContext context, CompletionCounter completed)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(completed);
        context.Fork(() =>
        {
            _ = completed.Increment();
            return Task.CompletedTask;
        });
    }

    /// <summary>Forks a worker that fails synchronously with a worker-specific message.</summary>
    /// <param name="context">The braid run context.</param>
    /// <param name="workerIndex">The worker index embedded in the failure message.</param>
    protected static void ForkSyncFailWorker(RunContext context, int workerIndex)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Fork(() => throw new InvalidOperationException($"sync-fail-{workerIndex}"));
    }

    /// <summary>Forks a worker that hits a deterministic sequence of named probes.</summary>
    /// <param name="context">The braid run context.</param>
    /// <param name="workerIndex">The worker index used in probe names.</param>
    /// <param name="probeCount">The number of probes to hit.</param>
    protected static void ForkWorkerDeterministicProbes(RunContext context, int workerIndex, int probeCount = 4)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Fork(async () =>
        {
            for (var probeIndex = 0; probeIndex < probeCount; probeIndex++)
                await Probe.HitAsync($"w{workerIndex}-p{probeIndex}", DefaultCancellationToken);
        });
    }

    /// <summary>Forks a worker that hits random-named probes and increments the completion counter.</summary>
    /// <param name="context">The braid run context.</param>
    /// <param name="workerIndex">The worker index used in probe names.</param>
    /// <param name="completed">The shared completion counter.</param>
    /// <param name="probeCount">The number of probes to hit.</param>
    protected static void ForkWorkerRandomProbes(RunContext context, int workerIndex, CompletionCounter completed, int probeCount = 5)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(completed);
        context.Fork(async () =>
        {
            for (var probeIndex = 0; probeIndex < probeCount; probeIndex++)
                await Probe.HitAsync($"w{workerIndex}-p{probeIndex}", DefaultCancellationToken);

            _ = completed.Increment();
        });
    }

    /// <summary>Forks a worker that hits a sequential series of step probes.</summary>
    /// <param name="context">The braid run context.</param>
    /// <param name="workerIndex">The worker index used in probe names.</param>
    /// <param name="probeCount">The number of probes to hit.</param>
    protected static void ForkWorkerSequentialProbes(RunContext context, int workerIndex, int probeCount)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Fork(async () =>
        {
            for (var probeIndex = 0; probeIndex < probeCount; probeIndex++)
                await Probe.HitAsync($"step-{workerIndex}-{probeIndex}", DefaultCancellationToken);
        });
    }

    /// <summary>Runs two threads that hit braid probes concurrently under a captured execution context.</summary>
    /// <param name="firstProbe">The first probe name.</param>
    /// <param name="secondProbe">The second probe name.</param>
    /// <exception cref="InvalidOperationException"><see cref="ExecutionContext.Capture" /> returned <see langword="null" />.</exception>
    protected static Task RunTwoThreadProbeRaceAsync(string firstProbe, string secondProbe)
    {
        var token = DefaultCancellationToken;
        var ec = ExecutionContext.Capture() ?? throw new InvalidOperationException("ExecutionContext.Capture returned null.");
        var readyCount = new CompletionCounter();
        var threadFailure = new Exception?[1];

        return StartNewOnThreadPoolAsync(
            () =>
            {
                var first = new Thread(() => BraidTestInternals.HitProbeOnCapturedContext(ec, firstProbe, readyCount, threadFailure, token));
                var second = new Thread(() => BraidTestInternals.HitProbeOnCapturedContext(ec, secondProbe, readyCount, threadFailure, token));

                first.Start();
                second.Start();

                first.Join();
                second.Join();

                if (threadFailure[0] is { } failure)
                    ExceptionDispatchInfo.Capture(failure).Throw();
            },
            DefaultCancellationToken);
    }

    /// <summary>Schedules a fork from a thread-pool thread and increments the completion counter.</summary>
    /// <param name="context">The braid run context.</param>
    /// <param name="completed">The shared completion counter.</param>
    /// <returns>A task that completes when the fork has been scheduled.</returns>
    protected static Task ScheduleConcurrentForkAsync(RunContext context, CompletionCounter completed)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(completed);
        return StartNewOnThreadPoolAsync(
            () =>
            {
                context.Fork(async () =>
                {
                    await Probe.HitAsync("ready", DefaultCancellationToken);
                    _ = completed.Increment();
                });
            },
            DefaultCancellationToken);
    }

    /// <summary>Starts synchronous work on the thread pool.</summary>
    /// <param name="action">The action to run.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that represents the scheduled work.</returns>
    protected static Task StartNewOnThreadPoolAsync(Action action, CancellationToken cancellationToken) => Task.Factory.StartNew(
        action,
        cancellationToken,
        TaskCreationOptions.DenyChildAttach,
        TaskScheduler.Default);

    /// <summary>Starts asynchronous work on the thread pool.</summary>
    /// <param name="action">The async action to run.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that represents the scheduled work.</returns>
    protected static Task StartNewOnThreadPoolAsync(Func<Task> action, CancellationToken cancellationToken) => Task.Factory.StartNew(
        action,
        cancellationToken,
        TaskCreationOptions.DenyChildAttach,
        TaskScheduler.Default).Unwrap();

    /// <summary>Starts a background loop that yields until cancellation is requested.</summary>
    /// <param name="noiseToken">The token that stops the yield loop.</param>
    /// <returns>A task that represents the yield loop.</returns>
    protected static Task StartNoiseYieldLoopAsync(CancellationToken noiseToken) => StartNewOnThreadPoolAsync(
        async () =>
        {
            while (!noiseToken.IsCancellationRequested)
                await Task.Yield();
        },
        DefaultCancellationToken);

    private static class BraidTestInternals
    {
        private const long ThreadReadyTimeoutMilliseconds = 10_000;

        public static void HitProbeOnCapturedContext(
            ExecutionContext executionContext,
            string probe,
            CompletionCounter readyCount,
            Exception?[] threadFailure,
            CancellationToken cancellationToken)
        {
            ExecutionContext.Run(
                executionContext,
                __ =>
                {
                    var completed = new ManualResetEventSlim(false);
                    Exception? captured = null;

                    var probeTask = HitOnContextAsync();
                    try
                    {
                        bool signaled;
                        try
                        {
                            signaled = completed.Wait(TimeSpan.FromMilliseconds(ThreadReadyTimeoutMilliseconds), cancellationToken);
                        }
                        catch (OperationCanceledException ex)
                        {
                            captured ??= ex;
                            signaled = true;
                        }

                        if (!signaled)
                            captured ??= new TimeoutException($"Probe '{probe}' did not complete before the thread watchdog timeout.");
                    }
                    finally
                    {
                        DisposeWhenCompleted(probeTask, completed);
                    }

                    if (captured != null)
                        _ = LazyInitializer.EnsureInitialized(ref MemoryMarshal.GetArrayDataReference(threadFailure), () => captured);

                    return;

                    async Task HitOnContextAsync()
                    {
                        try
                        {
                            WaitUntilBothThreadsAreReady(readyCount, cancellationToken);
                            await Probe.HitAsync(probe, cancellationToken);
                        }
                        catch (Exception ex) when (ex is RunException or OperationCanceledException or ArgumentException or InvalidOperationException or TimeoutException)
                        {
                            captured = ex;
                        }
                        finally
                        {
                            completed.Set();
                        }
                    }
                },
                null);
        }

        public static void RethrowIfFaultedOrCanceled(Task task)
        {
            if (task.IsFaulted)
                ExceptionDispatchInfo.Capture(task.Exception.GetBaseException()).Throw();

            if (task.IsCanceled)
                throw new TaskCanceledException(task);
        }

        public static async Task RunWatchdogAsync(Func<Task> startTask, string failureMessage, TimeSpan watchdogTimeout, bool prefixWatchdogMessage = true)
        {
            ArgumentNullException.ThrowIfNull(startTask);
            var task = startTask();
            var watchdog = Task.Delay(watchdogTimeout, TimeProvider.System, DefaultCancellationToken);
            if (await Task.WhenAny(task, watchdog).ConfigureAwait(false) != task)
                Assert.Fail(prefixWatchdogMessage ? $"Braid run did not complete before watchdog timeout. {failureMessage}" : failureMessage);

            await task.ConfigureAwait(false);
        }

        private static void WaitUntilBothThreadsAreReady(CompletionCounter readyCount, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(readyCount);
            _ = readyCount.Increment();

            SpinWait spinWait = default;
            var deadline = Environment.TickCount64 + ThreadReadyTimeoutMilliseconds;
            while (readyCount.Value < 2)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Environment.TickCount64 > deadline)
                    throw new TimeoutException("Probe threads did not both reach the readiness barrier.");

                spinWait.SpinOnce();
            }
        }

        private static void DisposeWhenCompleted(Task probeTask, ManualResetEventSlim signal) =>
            _ = probeTask.ContinueWith(
                static (_, state) =>
                {
                    if (state is ManualResetEventSlim s)
                        s.Dispose();
                },
                signal,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
    }
}
