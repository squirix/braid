using System.Runtime.ExceptionServices;
using Xunit;

namespace Braid.Tests;

/// <summary>
/// Deterministic tests for runtime boundaries: scope cleanup, snapshots, reuse, parallelism, and exception precedence.
/// </summary>
public sealed class BraidRuntimeBoundaryTests : TestBase
{
    /// <summary>
    /// Verifies AsyncLocal scope is cleared after a successful run.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncClearsRunScopeAfterSuccessfulRun()
    {
        await Braid.RunAsync(
            static async context =>
            {
                context.Fork(static async () => { await BraidProbe.HitAsync("ready", DefaultCancellationToken); });

                await context.JoinAsync(DefaultCancellationToken);
            },
            new BraidOptions { Iterations = 1, Seed = 12345 },
            DefaultCancellationToken);

        await BraidProbe.HitAsync("outside-run", DefaultCancellationToken);
    }

    /// <summary>
    /// Verifies AsyncLocal scope is cleared after a timeout failure.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncClearsRunScopeAfterTimeout()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var runTask = Braid.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    await BraidProbe.HitAsync("block", DefaultCancellationToken);
                    await gate.Task.WaitAsync(DefaultCancellationToken);
                });

                await context.JoinAsync(DefaultCancellationToken);
            },
            new BraidOptions { Iterations = 1, Seed = 12345, Timeout = TimeSpan.FromMilliseconds(50) },
            DefaultCancellationToken);

        var watchdog = Task.Delay(TimeSpan.FromSeconds(2), DefaultCancellationToken);
        if (await Task.WhenAny(runTask, watchdog) != runTask)
        {
            _ = gate.TrySetResult();
            Assert.Fail("Braid run did not complete before watchdog timeout.");
        }

        _ = gate.TrySetResult();

        try
        {
            await runTask;
            Assert.Fail("Expected timeout.");
        }
        catch (BraidRunException)
        {
        }

        await BraidProbe.HitAsync("outside-run", DefaultCancellationToken);
    }

    /// <summary>
    /// Verifies failure reports snapshot schedule and are not affected by later caller mutations.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task BraidRunExceptionSnapshotsTraceAndSchedule()
    {
        var backing = new[] { new BraidStep("worker-1", "ready") };
        var schedule = BraidSchedule.Replay(backing);

        var exception = await Assert.ThrowsAsync<BraidRunException>(async () =>
        {
            await Braid.RunAsync(
                static async context =>
                {
                    context.Fork(static async () =>
                    {
                        await BraidProbe.HitAsync("ready", DefaultCancellationToken);
                        throw new InvalidOperationException("after-ready");
                    });

                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions { Iterations = 1, Seed = 12345, Schedule = schedule },
                DefaultCancellationToken);
        });

        backing[0] = new BraidStep("worker-9", "mutated");

        var report = exception.ToString();
        Assert.Contains("worker-1 @ ready", report, StringComparison.Ordinal);
        Assert.DoesNotContain("worker-9", report, StringComparison.Ordinal);
        Assert.Equal(new BraidStep("worker-1", "ready"), exception.Schedule[0]);
    }

    /// <summary>
    /// Verifies schedule steps exposed from <see cref="BraidSchedule"/> cannot be mutated as a list.
    /// </summary>
    [Fact]
    public void BraidScheduleStepsCannotBeMutatedThroughPublicSurface()
    {
        var schedule = BraidSchedule.Replay(new BraidStep("worker-1", "ready"));
        var steps = schedule.Steps;

        Assert.Equal(new BraidStep("worker-1", "ready"), steps[0]);

        if (steps is IList<BraidStep> list)
        {
            _ = Assert.Throws<NotSupportedException>(() => list.Add(new BraidStep("worker-2", "x")));
            _ = Assert.Throws<NotSupportedException>(list.Clear);
            _ = Assert.Throws<NotSupportedException>(() => list[0] = new BraidStep("worker-9", "mutated"));
        }

        Assert.Equal(new BraidStep("worker-1", "ready"), schedule.Steps[0]);
    }

    /// <summary>
    /// Verifies the same options instance can be reused across separate runs.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task SameBraidOptionsInstanceCanBeReusedAcrossRuns()
    {
        var options = new BraidOptions { Iterations = 1, Seed = 12345 };

        for (var pass = 0; pass < 2; pass++)
        {
            var value = 0;
            await Braid.RunAsync(
                async context =>
                {
                    context.Fork(async () =>
                    {
                        await BraidProbe.HitAsync("ready", DefaultCancellationToken);
                        _ = Interlocked.Increment(ref value);
                    });

                    await context.JoinAsync(DefaultCancellationToken);
                },
                options,
                DefaultCancellationToken);

            Assert.Equal(1, value);
        }
    }

    /// <summary>
    /// Verifies the same schedule instance can be reused across runs with identical ordering.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task SameBraidScheduleInstanceCanBeReusedAcrossRuns()
    {
        var schedule = BraidSchedule.Replay(new BraidStep("worker-2", "ready"), new BraidStep("worker-1", "ready"));

        for (var pass = 0; pass < 2; pass++)
        {
            var order = new List<string>();
            await Braid.RunAsync(
                async context =>
                {
                    context.Fork(async () =>
                    {
                        await BraidProbe.HitAsync("ready", DefaultCancellationToken);
                        order.Add("worker-1");
                    });

                    context.Fork(async () =>
                    {
                        await BraidProbe.HitAsync("ready", DefaultCancellationToken);
                        order.Add("worker-2");
                    });

                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions { Iterations = 1, Seed = 999 + pass, Schedule = schedule },
                DefaultCancellationToken);

            Assert.Equal(["worker-2", "worker-1"], order);
        }
    }

    /// <summary>
    /// Verifies a scripted schedule is not consumed by a single run.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ScriptedScheduleIsNotConsumedByRun()
    {
        var schedule = BraidSchedule.Replay(new BraidStep("worker-2", "ready"), new BraidStep("worker-1", "ready"));

        Assert.Equal(["worker-2", "worker-1"], await RunOnceAsync(111));
        Assert.Equal(["worker-2", "worker-1"], await RunOnceAsync(222));
        return;

        async Task<List<string>> RunOnceAsync(int seed)
        {
            var order = new List<string>();
            await Braid.RunAsync(
                async context =>
                {
                    context.Fork(async () =>
                    {
                        await BraidProbe.HitAsync("ready", DefaultCancellationToken);
                        order.Add("worker-1");
                    });

                    context.Fork(async () =>
                    {
                        await BraidProbe.HitAsync("ready", DefaultCancellationToken);
                        order.Add("worker-2");
                    });

                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions { Iterations = 1, Seed = seed, Schedule = schedule },
                DefaultCancellationToken);

            return order;
        }
    }

    /// <summary>
    /// Verifies many concurrent runs do not share scheduler state.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ManyIndependentRunsInParallelDoNotShareState()
    {
        var runs = new Task[20];
        for (var i = 0; i < runs.Length; i++)
        {
            var seed = 10_000 + i;
            var local = 0;
            runs[i] = Braid.RunAsync(
                async context =>
                {
                    context.Fork(async () =>
                    {
                        await BraidProbe.HitAsync("ready", DefaultCancellationToken);
                        _ = Interlocked.Increment(ref local);
                    });

                    context.Fork(async () =>
                    {
                        await BraidProbe.HitAsync("ready", DefaultCancellationToken);
                        _ = Interlocked.Increment(ref local);
                    });

                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions { Iterations = 1, Seed = seed },
                DefaultCancellationToken);
        }

        var allRuns = Task.WhenAll(runs);
        var watchdog = Task.Delay(TimeSpan.FromSeconds(15), DefaultCancellationToken);
        if (await Task.WhenAny(allRuns, watchdog) != allRuns)
        {
            Assert.Fail("Braid run did not complete before watchdog timeout.");
        }

        await allRuns;
    }

    /// <summary>
    /// Verifies parallel scripted runs each follow their own schedule.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ParallelScriptedRunsUseTheirOwnSchedules()
    {
        var scheduleA = BraidSchedule.Replay(new BraidStep("worker-1", "ready"), new BraidStep("worker-2", "ready"));
        var scheduleB = BraidSchedule.Replay(new BraidStep("worker-2", "ready"), new BraidStep("worker-1", "ready"));

        var orderA = new List<string>();
        var orderB = new List<string>();

        var runA = Braid.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    await BraidProbe.HitAsync("ready", DefaultCancellationToken);
                    orderA.Add("worker-1");
                });

                context.Fork(async () =>
                {
                    await BraidProbe.HitAsync("ready", DefaultCancellationToken);
                    orderA.Add("worker-2");
                });

                await context.JoinAsync(DefaultCancellationToken);
            },
            new BraidOptions { Iterations = 1, Seed = 101, Schedule = scheduleA },
            DefaultCancellationToken);

        var runB = Braid.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    await BraidProbe.HitAsync("ready", DefaultCancellationToken);
                    orderB.Add("worker-1");
                });

                context.Fork(async () =>
                {
                    await BraidProbe.HitAsync("ready", DefaultCancellationToken);
                    orderB.Add("worker-2");
                });

                await context.JoinAsync(DefaultCancellationToken);
            },
            new BraidOptions { Iterations = 1, Seed = 202, Schedule = scheduleB },
            DefaultCancellationToken);

        var combined = Task.WhenAll(runA, runB);
        var watchdog = Task.Delay(TimeSpan.FromSeconds(2), DefaultCancellationToken);
        if (await Task.WhenAny(combined, watchdog) != combined)
        {
            Assert.Fail("Braid run did not complete before watchdog timeout.");
        }

        await combined;

        Assert.Equal(["worker-1", "worker-2"], orderA);
        Assert.Equal(["worker-2", "worker-1"], orderB);
    }

    /// <summary>
    /// Verifies probes started under suppressed flow do not bind to the braid worker.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ProbeInsideSuppressedExecutionContextCompletesOutsideRun()
    {
        await Braid.RunAsync(
            static async context =>
            {
                context.Fork(static async () =>
                {
                    Task suppressedProbeTask;

                    using (ExecutionContext.SuppressFlow())
                    {
                        suppressedProbeTask = Task.Run(
                            static () => BraidProbe
                                        .HitAsync("suppressed", DefaultCancellationToken)
                                        .AsTask(),
                            DefaultCancellationToken);
                    }

                    await suppressedProbeTask;

                    await BraidProbe.HitAsync("real", DefaultCancellationToken);
                });

                await context.JoinAsync(DefaultCancellationToken);
            },
            new BraidOptions
            {
                Iterations = 1,
                Seed = 12345,
                Schedule = BraidSchedule.Replay(new BraidStep("worker-1", "real")),
            },
            DefaultCancellationToken);
    }

    /// <summary>
    /// Verifies a flowing child task that hits a probe while the parent waits at another probe fails clearly.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ProbeInsideFlowingChildTaskConcurrentWithParentFailsClearlyOrSerializes()
    {
        var runTask = Braid.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    var token = DefaultCancellationToken;
                    var ec = ExecutionContext.Capture() ?? throw new InvalidOperationException("ExecutionContext.Capture returned null.");

                    var readyCount = 0;
                    Exception? threadFailure = null;

                    await Task.Run(
                        () =>
                        {
                            var parentThread = new Thread(() => HitProbe("parent"));
                            var childThread = new Thread(() => HitProbe("child"));

                            parentThread.Start();
                            childThread.Start();

                            parentThread.Join();
                            childThread.Join();

                            if (threadFailure is not null)
                            {
                                ExceptionDispatchInfo.Capture(threadFailure).Throw();
                            }
                        },
                        DefaultCancellationToken);

                    return;

                    void HitProbe(string probe)
                    {
                        ExecutionContext.Run(
                            ec,
                            __ =>
                            {
                                try
                                {
                                    WaitUntilBothThreadsAreReady(ref readyCount, token);
                                    BraidProbe.HitAsync(probe, token).AsTask().GetAwaiter().GetResult();
                                }
                                catch (Exception ex)
                                {
                                    _ = Interlocked.CompareExchange(ref threadFailure, ex, null);
                                }
                            },
                            null);
                    }
                });

                await context.JoinAsync(DefaultCancellationToken);
            },
            new BraidOptions { Iterations = 1, Seed = 12345 },
            DefaultCancellationToken);

        try
        {
            await runTask;
        }
        catch (BraidRunException exception)
        {
            Assert.Contains("Concurrent probe hit on the same worker is not supported.", exception.ToString(), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Verifies a serialized child task probe after the parent probe completes is allowed.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ProbeInsideFlowingChildTaskAfterParentProbeCompletesSucceeds()
    {
        var runTask = Braid.RunAsync(
            static async context =>
            {
                context.Fork(static async () =>
                {
                    await BraidProbe.HitAsync("parent", DefaultCancellationToken);
                    await Task.Run(static () => BraidProbe.HitAsync("child", DefaultCancellationToken).AsTask(), DefaultCancellationToken);
                });

                await context.JoinAsync(DefaultCancellationToken);
            },
            new BraidOptions { Iterations = 1, Seed = 12345 },
            DefaultCancellationToken);

        await AssertCompletesBeforeWatchdogAsync(runTask, "Serialized child probe should complete.");
    }

    /// <summary>
    /// Verifies cancellation is observed before the user callback runs.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncSurfacesCancellationBeforeAnyFork()
    {
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        var executed = false;

        _ = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await Braid.RunAsync(
                context =>
                {
                    executed = true;
                    _ = context;
                    return Task.CompletedTask;
                },
                new BraidOptions { Iterations = 1, Seed = 12345 },
                canceled.Token);
        });

        Assert.False(executed);
    }

    /// <summary>
    /// Verifies forked startup workers are stopped when the callback throws before join.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncStopsStartupWorkersWhenCallbackThrowsImmediatelyAfterFork()
    {
        var runTask = Braid.RunAsync(
            static context =>
            {
                context.Fork(static async () => { await BraidProbe.HitAsync("ready", DefaultCancellationToken); });

                context.Fork(static async () => { await BraidProbe.HitAsync("ready", DefaultCancellationToken); });

                context.Fork(static async () => { await BraidProbe.HitAsync("ready", DefaultCancellationToken); });

                throw new InvalidOperationException("callback failed before join");
            },
            new BraidOptions { Iterations = 1, Seed = 12345 },
            DefaultCancellationToken);

        var watchdog = Task.Delay(TimeSpan.FromSeconds(2), DefaultCancellationToken);
        if (await Task.WhenAny(runTask, watchdog) != runTask)
        {
            Assert.Fail("Run should not hang after callback throws.");
        }

        try
        {
            await runTask;
            Assert.Fail("Expected BraidRunException.");
        }
        catch (BraidRunException ex)
        {
            var report = ex.ToString();
            Assert.Contains("callback failed before join", report, StringComparison.Ordinal);
            Assert.Contains("worker-1 forked", report, StringComparison.Ordinal);
            Assert.Contains("Trace:", report, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Verifies timeout reports include worker and probe trace context.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task TimeoutReportIncludesRunningWorkerTrace()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            var runTask = Braid.RunAsync(
                async context =>
                {
                    context.Fork(async () =>
                    {
                        await BraidProbe.HitAsync("started", DefaultCancellationToken);
                        await gate.Task.WaitAsync(DefaultCancellationToken);
                    });

                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions { Iterations = 1, Seed = 12345, Timeout = TimeSpan.FromMilliseconds(50) },
                DefaultCancellationToken);

            var watchdog = Task.Delay(TimeSpan.FromSeconds(2), DefaultCancellationToken);
            if (await Task.WhenAny(runTask, watchdog) != runTask)
            {
                Assert.Fail("Braid run did not complete before watchdog timeout.");
            }

            try
            {
                await runTask;
                Assert.Fail("Expected BraidRunException.");
            }
            catch (BraidRunException exception)
            {
                var report = exception.ToString();
                Assert.Contains("braid run timed out.", report, StringComparison.Ordinal);
                Assert.Contains("started", report, StringComparison.Ordinal);
                Assert.Contains("worker-1", report, StringComparison.Ordinal);
                Assert.Contains("released", report, StringComparison.Ordinal);
            }
        }
        finally
        {
            _ = gate.TrySetResult();
        }
    }

    /// <summary>
    /// Verifies worker failure is reported when it occurs before the iteration timeout.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task WorkerFailureBeforeTimeoutWinsOverTimeout()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await Braid.RunAsync(
                static async context =>
                {
                    context.Fork(static async () =>
                    {
                        await BraidProbe.HitAsync("before-failure", DefaultCancellationToken);
                        throw new InvalidOperationException("worker failed before timeout");
                    });

                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions { Iterations = 1, Seed = 12345, Timeout = TimeSpan.FromSeconds(1) },
                DefaultCancellationToken);
        });

        var report = exception.ToString();
        Assert.Contains("worker failed before timeout", report, StringComparison.Ordinal);
        Assert.DoesNotContain("braid run timed out.", report, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies timeout wins when the worker failure happens only after the timeout window.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task TimeoutBeforeLateWorkerFailureWinsOverLateFailure()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            var runTask = Braid.RunAsync(
                async context =>
                {
                    context.Fork(async () =>
                    {
                        await BraidProbe.HitAsync("block", DefaultCancellationToken);
                        await gate.Task.WaitAsync(DefaultCancellationToken);
                        throw new InvalidOperationException("too late after timeout");
                    });

                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions { Iterations = 1, Seed = 12345, Timeout = TimeSpan.FromMilliseconds(50) },
                DefaultCancellationToken);

            var watchdog = Task.Delay(TimeSpan.FromSeconds(2), DefaultCancellationToken);
            if (await Task.WhenAny(runTask, watchdog) != runTask)
            {
                Assert.Fail("Braid run did not complete before watchdog timeout.");
            }

            try
            {
                await runTask;
                Assert.Fail("Expected BraidRunException.");
            }
            catch (BraidRunException exception)
            {
                Assert.Contains("braid run timed out.", exception.Message, StringComparison.Ordinal);
                Assert.DoesNotContain("too late after timeout", exception.ToString(), StringComparison.Ordinal);
            }
        }
        finally
        {
            _ = gate.TrySetResult();
        }
    }

    /// <summary>
    /// Verifies external cancellation wins over a subsequent worker failure.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ExternalCancellationWinsWhenCancellationTokenIsCanceled()
    {
        using var runCts = new CancellationTokenSource();
        var runToken = runCts.Token;

        var runTask = Braid.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    await BraidProbe.HitAsync("gate", DefaultCancellationToken);

                    while (!runToken.IsCancellationRequested)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(5), DefaultCancellationToken);
                    }

                    throw new InvalidOperationException("worker after cancel");
                });

                await context.JoinAsync(runToken);
            },
            new BraidOptions { Iterations = 1, Seed = 12345 },
            runToken);

        await Task.Delay(TimeSpan.FromMilliseconds(40), DefaultCancellationToken);
        await runCts.CancelAsync();

        var oceTask = Assert.ThrowsAsync<OperationCanceledException>(() => runTask);
        var watchdog = Task.Delay(TimeSpan.FromSeconds(2), DefaultCancellationToken);

        if (await Task.WhenAny(oceTask, watchdog) != oceTask)
        {
            Assert.Fail("Braid run did not complete before watchdog timeout.");
        }

        _ = await oceTask;
    }

    /// <summary>
    /// Verifies <see cref="BraidRunException.ToString"/> does not mutate between calls.
    /// </summary>
    [Fact]
    public void BraidRunExceptionToStringIsStable()
    {
        var exception = new BraidRunException("failed", 42, 3, ["worker-1 forked"], [new BraidStep("worker-1", "ready")], new InvalidOperationException("inner"));

        var first = exception.ToString();
        var second = exception.ToString();

        Assert.Equal(first, second);
    }

    /// <summary>
    /// Verifies an unused schedule with no forked workers fails clearly.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncFailsWhenScheduleProvidedButNoWorkersForked()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await Braid.RunAsync(
                static _ => Task.CompletedTask,
                new BraidOptions
                {
                    Iterations = 1,
                    Seed = 12345,
                    Schedule = BraidSchedule.Replay(new BraidStep("worker-1", "ready")),
                },
                DefaultCancellationToken);
        });

        Assert.Contains("unused steps", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies a run with no workers and no schedule completes.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncCompletesWhenNoWorkersForkedAndNoSchedule() => await Braid.RunAsync(
        static _ => Task.CompletedTask,
        new BraidOptions { Iterations = 1, Seed = 12345 },
        DefaultCancellationToken);

    private static async Task AssertCompletesBeforeWatchdogAsync(Task runTask, string failureMessage)
    {
        var watchdog = Task.Delay(TimeSpan.FromSeconds(2), DefaultCancellationToken);
        if (await Task.WhenAny(runTask, watchdog) != runTask)
        {
            Assert.Fail($"Braid run did not complete before watchdog timeout. {failureMessage}");
        }

        await runTask;
    }

    private static void WaitUntilBothThreadsAreReady(ref int readyCount, CancellationToken cancellationToken)
    {
        _ = Interlocked.Increment(ref readyCount);

        var spinWait = default(SpinWait);
        while (Volatile.Read(ref readyCount) < 2)
        {
            cancellationToken.ThrowIfCancellationRequested();
            spinWait.SpinOnce();
        }
    }
}
