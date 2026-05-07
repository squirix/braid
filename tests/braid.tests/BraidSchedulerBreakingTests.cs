using System.Threading;
using Xunit;

namespace Braid.Tests;

/// <summary>
/// Deterministic stress tests for scheduler edge cases and invalid usage patterns.
/// </summary>
public sealed class BraidSchedulerBreakingTests : TestBase
{
    /// <summary>
    /// Verifies user JoinAsync plus outer RunAsync join does not double-release workers or throw semaphore errors.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncDoesNotReleaseWorkersTwiceWhenUserAlreadyJoined()
    {
        var runTask = Braid.RunAsync(
            static async context =>
            {
                context.Fork(static async () =>
                {
                    await BraidProbe.HitAsync("ready", DefaultCancellationToken);
                });

                await context.JoinAsync(DefaultCancellationToken);
            },
            new BraidOptions { Iterations = 1, Seed = 12345 },
            DefaultCancellationToken);

        await AssertRunCompletesBeforeWatchdogAsync(
            runTask,
            "Double join should complete without SemaphoreFullException or BraidRunException.");
    }

    /// <summary>
    /// Verifies a second JoinAsync after the first completed join is idempotent for a simple completed run.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task JoinAsyncCanBeCalledTwiceAfterCompletionOrFailsClearly()
    {
        var runTask = Braid.RunAsync(
            static async context =>
            {
                context.Fork(static async () =>
                {
                    await BraidProbe.HitAsync("ready", DefaultCancellationToken);
                });

                await context.JoinAsync(DefaultCancellationToken);
                await context.JoinAsync(DefaultCancellationToken);
            },
            new BraidOptions { Iterations = 1, Seed = 12345 },
            DefaultCancellationToken);

        await AssertRunCompletesBeforeWatchdogAsync(
            runTask,
            "Sequential second JoinAsync should not deadlock or throw SemaphoreFullException.");
    }

    /// <summary>
    /// Verifies a worker failure before any probe is surfaced on join with trace context.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncReportsWorkerFailureBeforeFirstProbe()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await Braid.RunAsync(
                static async context =>
                {
                    context.Fork(static () => Task.FromException(new InvalidOperationException("before-probe failure")));

                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions { Iterations = 1, Seed = 12345 },
                DefaultCancellationToken);
        });

        var report = exception.ToString();
        Assert.Contains("before-probe failure", report, StringComparison.Ordinal);
        Assert.Contains("worker-1", report, StringComparison.Ordinal);
        Assert.Contains("Trace:", report, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies HitAsync from the run callback without a current worker completes immediately (no current task).
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ProbeHitInsideRunButOutsideWorkerCompletesImmediately()
    {
        var runTask = Braid.RunAsync(
            static async context =>
            {
                await BraidProbe.HitAsync("callback-probe", DefaultCancellationToken);

                context.Fork(static async () =>
                {
                    await BraidProbe.HitAsync("worker-probe", DefaultCancellationToken);
                });

                await context.JoinAsync(DefaultCancellationToken);
            },
            new BraidOptions { Iterations = 1, Seed = 12345 },
            DefaultCancellationToken);

        await AssertRunCompletesBeforeWatchdogAsync(
            runTask,
            "Probe outside a forked worker should not deadlock.");
    }

    /// <summary>
    /// Verifies duplicate scripted steps for the same worker and probe are rejected or fail clearly after the worker completes.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task DuplicateScriptedReleaseForSameProbeFailsClearly()
    {
        var options = new BraidOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = BraidSchedule.Replay(
                new BraidStep("worker-1", "ready"),
                new BraidStep("worker-1", "ready")),
        };

        var exception = await Assert.ThrowsAsync<BraidRunException>(async () =>
        {
            await Braid.RunAsync(
                static async context =>
                {
                    context.Fork(static async () =>
                    {
                        await BraidProbe.HitAsync("ready", DefaultCancellationToken);
                    });

                    await context.JoinAsync(DefaultCancellationToken);
                },
                options,
                DefaultCancellationToken);
        });

        Assert.Contains("Scripted schedule contained unused steps after all workers completed.", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a scripted schedule with steps that no worker can satisfy after the run completes is reported as a failure.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncFailsWhenScriptedScheduleHasUnusedSteps()
    {
        var options = new BraidOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = BraidSchedule.Replay(
                new BraidStep("worker-1", "ready"),
                new BraidStep("worker-2", "never")),
        };

        var exception = await Assert.ThrowsAsync<BraidRunException>(async () =>
        {
            await Braid.RunAsync(
                static async context =>
                {
                    context.Fork(static async () =>
                    {
                        await BraidProbe.HitAsync("ready", DefaultCancellationToken);
                    });

                    await context.JoinAsync(DefaultCancellationToken);
                },
                options,
                DefaultCancellationToken);
        });

        Assert.Contains("Scripted schedule contained unused steps after all workers completed.", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies timeout surfaces as BraidRunException and the run does not hang when StopAsync waits on a non-cooperative worker.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncTimeoutDoesNotHangWhenRunningWorkerIgnoresCancellation()
    {
        var unblock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var runTask = Braid.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    await BraidProbe.HitAsync("at-probe", DefaultCancellationToken);
                    await unblock.Task;
                });

                await context.JoinAsync(DefaultCancellationToken);
            },
            new BraidOptions { Iterations = 1, Seed = 12345, Timeout = TimeSpan.FromMilliseconds(50) },
            DefaultCancellationToken);

        var watchdog = Task.Delay(TimeSpan.FromSeconds(2), DefaultCancellationToken);
        var winner = await Task.WhenAny(runTask, watchdog);

        if (winner != runTask)
        {
            _ = unblock.TrySetResult();
            Assert.Fail("Braid run did not complete before watchdog timeout.");
        }

        _ = unblock.TrySetResult();

        try
        {
            await runTask;
            Assert.Fail("Expected BraidRunException for timeout.");
        }
        catch (BraidRunException ex)
        {
            Assert.Contains("braid run timed out.", ex.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Verifies two concurrent JoinAsync calls from the same callback either both complete or fail clearly.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ConcurrentJoinAsyncCallsDoNotCorruptScheduler()
    {
        var runTask = Braid.RunAsync(
            static async context =>
            {
                context.Fork(static async () =>
                {
                    await BraidProbe.HitAsync("ready", DefaultCancellationToken);
                });

                context.Fork(static async () =>
                {
                    await BraidProbe.HitAsync("ready", DefaultCancellationToken);
                });

                var join1 = context.JoinAsync(DefaultCancellationToken);
                var join2 = context.JoinAsync(DefaultCancellationToken);
                await Task.WhenAll(join1, join2);
            },
            new BraidOptions { Iterations = 1, Seed = 12345 },
            DefaultCancellationToken);

        await AssertRunCompletesBeforeWatchdogAsync(
            runTask,
            "Concurrent JoinAsync should not deadlock or surface SemaphoreFullException.");
    }

    /// <summary>
    /// Verifies nested braid runs are rejected and do not corrupt the outer scope.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task NestedRunInsideWorkerFailsClearlyOrDoesNotLeakOuterRun()
    {
        var runTask = Braid.RunAsync(
            static async context =>
            {
                context.Fork(async () =>
                {
                    await BraidProbe.HitAsync("outer-before-nested", DefaultCancellationToken);
                    _ = await Assert.ThrowsAsync<InvalidOperationException>(static async () =>
                    {
                        await Braid.RunAsync(
                            static async inner =>
                            {
                                inner.Fork(static async () =>
                                {
                                    await BraidProbe.HitAsync("inner-ready", DefaultCancellationToken);
                                });

                                await inner.JoinAsync(DefaultCancellationToken);
                            },
                            new BraidOptions { Iterations = 1, Seed = 999 },
                            DefaultCancellationToken);
                    });

                    await BraidProbe.HitAsync("outer-after-nested", DefaultCancellationToken);
                });

                await context.JoinAsync(DefaultCancellationToken);
            },
            new BraidOptions { Iterations = 1, Seed = 12345 },
            DefaultCancellationToken);

        await AssertRunCompletesBeforeWatchdogAsync(
            runTask,
            "Nested run inside worker should fail clearly without corrupting outer run.");
    }

    /// <summary>
    /// Verifies nested braid runs are rejected from the run callback before any fork.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task NestedRunInsideRunCallbackFailsClearlyOrDoesNotLeakScope()
    {
        var runTask = Braid.RunAsync(
            static async context =>
            {
                _ = await Assert.ThrowsAsync<InvalidOperationException>(static async () =>
                {
                    await Braid.RunAsync(
                        static inner =>
                        {
                            _ = inner;
                            return Task.CompletedTask;
                        },
                        new BraidOptions { Iterations = 1, Seed = 777 },
                        DefaultCancellationToken);
                });

                context.Fork(static async () =>
                {
                    await BraidProbe.HitAsync("ready", DefaultCancellationToken);
                });

                await context.JoinAsync(DefaultCancellationToken);
            },
            new BraidOptions { Iterations = 1, Seed = 12345 },
            DefaultCancellationToken);

        await AssertRunCompletesBeforeWatchdogAsync(
            runTask,
            "Nested run in callback should fail before corrupting outer scope.");
    }

    /// <summary>
    /// Verifies concurrent independent runs do not share scheduler or schedule state.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ParallelIndependentRunsDoNotShareSchedulerState()
    {
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
            new BraidOptions
            {
                Iterations = 1,
                Seed = 111,
                Schedule = BraidSchedule.Replay(
                    new BraidStep("worker-1", "ready"),
                    new BraidStep("worker-2", "ready")),
            },
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
            new BraidOptions
            {
                Iterations = 1,
                Seed = 222,
                Schedule = BraidSchedule.Replay(
                    new BraidStep("worker-2", "ready"),
                    new BraidStep("worker-1", "ready")),
            },
            DefaultCancellationToken);

        var combined = Task.WhenAll(runA, runB);
        var watchdog = Task.Delay(TimeSpan.FromSeconds(2), DefaultCancellationToken);
        var winner = await Task.WhenAny(combined, watchdog);

        if (winner != combined)
        {
            Assert.Fail("Braid run did not complete before watchdog timeout.");
        }

        await combined;

        Assert.Equal(["worker-1", "worker-2"], orderA);
        Assert.Equal(["worker-2", "worker-1"], orderB);
    }

    /// <summary>
    /// Verifies a second probe from a child task while the worker waits at a probe fails clearly.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ProbeFromChildTaskInsideWorkerFailsClearlyOrIsSerialized()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(async () =>
        {
            await Braid.RunAsync(
                async context =>
                {
                    context.Fork(async () =>
                    {
                        await Task.Run(
                            () =>
                            {
                                var token = DefaultCancellationToken;
                                var ec = ExecutionContext.Capture() ?? throw new InvalidOperationException("ExecutionContext.Capture returned null.");
                                var barrier = new Barrier(2);
                                Exception? threadFailure = null;

                                void HitProbe(string probe)
                                {
                                    ExecutionContext.Run(
                                        ec,
                                        _ =>
                                        {
                                            barrier.SignalAndWait();
                                            try
                                            {
                                                BraidProbe.HitAsync(probe, token).AsTask().GetAwaiter().GetResult();
                                            }
                                            catch (Exception ex)
                                            {
                                                threadFailure ??= ex;
                                            }
                                        },
                                        null);
                                }

                                var parentThread = new Thread(() => HitProbe("parent"));
                                var childThread = new Thread(() => HitProbe("child"));
                                parentThread.Start();
                                childThread.Start();
                                parentThread.Join();
                                childThread.Join();
                                if (threadFailure is not null)
                                {
                                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(threadFailure).Throw();
                                }
                            },
                            DefaultCancellationToken);
                    });

                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions { Iterations = 1, Seed = 12345 },
                DefaultCancellationToken);
        });

        Assert.Contains("Concurrent probe hit on the same worker is not supported.", exception.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies concurrent probe hits from the same worker are rejected.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ConcurrentProbesFromSameWorkerFailClearly()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(async () =>
        {
            await Braid.RunAsync(
                async context =>
                {
                    context.Fork(async () =>
                    {
                        await Task.Run(
                            () =>
                            {
                                var token = DefaultCancellationToken;
                                var ec = ExecutionContext.Capture() ?? throw new InvalidOperationException("ExecutionContext.Capture returned null.");
                                var barrier = new Barrier(2);
                                Exception? threadFailure = null;

                                void HitProbe(string probe)
                                {
                                    ExecutionContext.Run(
                                        ec,
                                        _ =>
                                        {
                                            barrier.SignalAndWait();
                                            try
                                            {
                                                BraidProbe.HitAsync(probe, token).AsTask().GetAwaiter().GetResult();
                                            }
                                            catch (Exception ex)
                                            {
                                                threadFailure ??= ex;
                                            }
                                        },
                                        null);
                                }

                                var first = new Thread(() => HitProbe("a"));
                                var second = new Thread(() => HitProbe("b"));
                                first.Start();
                                second.Start();
                                first.Join();
                                second.Join();
                                if (threadFailure is not null)
                                {
                                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(threadFailure).Throw();
                                }
                            },
                            DefaultCancellationToken);
                    });

                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions { Iterations = 1, Seed = 12345 },
                DefaultCancellationToken);
        });

        Assert.Contains("A forked operation failed.", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Concurrent probe hit on the same worker is not supported.", exception.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies invalid probe names are rejected outside a braid run.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task HitAsyncRejectsInvalidProbeNameOutsideRun()
    {
        _ = await Assert.ThrowsAnyAsync<ArgumentException>(static async () => await BraidProbe.HitAsync(null!, DefaultCancellationToken));
        _ = await Assert.ThrowsAnyAsync<ArgumentException>(static async () => await BraidProbe.HitAsync(string.Empty, DefaultCancellationToken));
        _ = await Assert.ThrowsAnyAsync<ArgumentException>(static async () => await BraidProbe.HitAsync(" ", DefaultCancellationToken));
    }

    /// <summary>
    /// Verifies invalid probe names are rejected inside a worker before scheduler state is corrupted.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task HitAsyncRejectsInvalidProbeNameInsideWorker()
    {
        var runTask = Braid.RunAsync(
            static async context =>
            {
                context.Fork(static async () =>
                {
                    _ = await Assert.ThrowsAnyAsync<ArgumentException>(static async () => await BraidProbe.HitAsync(null!, DefaultCancellationToken));
                    _ = await Assert.ThrowsAnyAsync<ArgumentException>(static async () => await BraidProbe.HitAsync(string.Empty, DefaultCancellationToken));
                    _ = await Assert.ThrowsAnyAsync<ArgumentException>(static async () => await BraidProbe.HitAsync(" ", DefaultCancellationToken));
                    await BraidProbe.HitAsync("ok", DefaultCancellationToken);
                });

                await context.JoinAsync(DefaultCancellationToken);
            },
            new BraidOptions { Iterations = 1, Seed = 12345 },
            DefaultCancellationToken);

        await AssertRunCompletesBeforeWatchdogAsync(
            runTask,
            "Invalid probe names inside worker should throw ArgumentException without corrupting the run.");
    }

    /// <summary>
    /// Verifies a canceled probe token does not strand the worker in a permanent wait.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task CanceledProbeDoesNotLeaveWorkerPermanentlyWaiting()
    {
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        var exception = await Assert.ThrowsAsync<BraidRunException>(async () =>
        {
            await Braid.RunAsync(
                async context =>
                {
                    context.Fork(async () =>
                    {
                        await BraidProbe.HitAsync("ready", canceled.Token);
                    });

                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions { Iterations = 1, Seed = 12345 },
                DefaultCancellationToken);
        });

        Assert.Contains("A forked operation failed.", exception.Message, StringComparison.Ordinal);
        Assert.NotNull(exception.InnerException);
        Assert.True(
            exception.InnerException is OperationCanceledException,
            $"Expected cancellation-derived exception, got {exception.InnerException.GetType().FullName}.");
    }

    /// <summary>
    /// Verifies cancellation at a probe preserves trace in the failure report.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task CanceledWorkerProbeFailureReportContainsProbeTrace()
    {
        using var workerCts = new CancellationTokenSource();
        workerCts.Cancel();

        var exceptionTask = Assert.ThrowsAsync<BraidRunException>(async () =>
        {
            await Braid.RunAsync(
                async context =>
                {
                    context.Fork(async () =>
                    {
                        await BraidProbe.HitAsync("ready", workerCts.Token);
                    });

                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions { Iterations = 1, Seed = 12345 },
                DefaultCancellationToken);
        });

        var watchdog = Task.Delay(TimeSpan.FromSeconds(2), DefaultCancellationToken);
        if (await Task.WhenAny(exceptionTask, watchdog) != exceptionTask)
        {
            Assert.Fail("Braid run did not complete before watchdog timeout.");
        }

        var exception = await exceptionTask;
        var report = exception.ToString();
        Assert.Contains("ready", report, StringComparison.Ordinal);
        Assert.Contains("worker-1", report, StringComparison.Ordinal);
        Assert.Contains("Trace:", report, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies external cancellation takes precedence over a worker failure observed after cancel.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ExternalCancellationTakesPrecedenceOverWorkerFailureAfterCancel()
    {
        using var runCts = new CancellationTokenSource();

        var runTask = Braid.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    await BraidProbe.HitAsync("block", DefaultCancellationToken);
                    while (!runCts.Token.IsCancellationRequested)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(5), DefaultCancellationToken);
                    }

                    throw new InvalidOperationException("after-cancel worker failure");
                });

                await context.JoinAsync(runCts.Token);
            },
            new BraidOptions { Iterations = 1, Seed = 12345 },
            runCts.Token);

        await Task.Delay(TimeSpan.FromMilliseconds(50), DefaultCancellationToken);
        runCts.Cancel();

        var oceTask = Assert.ThrowsAsync<OperationCanceledException>(async () => await runTask);
        var joinWatchdog = Task.Delay(TimeSpan.FromSeconds(2), DefaultCancellationToken);
        if (await Task.WhenAny(oceTask, joinWatchdog) != oceTask)
        {
            Assert.Fail("Braid run did not complete before watchdog timeout.");
        }

        _ = await oceTask;
    }

    /// <summary>
    /// Verifies forked workers are stopped when the user callback throws before join.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncStopsForkedWorkersWhenCallbackThrowsBeforeJoin()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(async () =>
        {
            await Braid.RunAsync(
                static context =>
                {
                    context.Fork(static async () =>
                    {
                        await BraidProbe.HitAsync("ready", DefaultCancellationToken);
                    });

                    throw new InvalidOperationException("callback failed");
                },
                new BraidOptions { Iterations = 1, Seed = 12345 },
                DefaultCancellationToken);
        });

        var report = exception.ToString();
        Assert.Contains("callback failed", report, StringComparison.Ordinal);
        Assert.Contains("worker-1 forked", report, StringComparison.Ordinal);
        Assert.Contains("Trace:", report, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies Braid.RunAsync joins forked workers after the callback returns without an explicit join.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncAutomaticallyJoinsWhenCallbackReturnsWithoutExplicitJoin()
    {
        var completed = 0;

        var runTask = Braid.RunAsync(
            context =>
            {
                context.Fork(async () =>
                {
                    await BraidProbe.HitAsync("a", DefaultCancellationToken);
                    _ = Interlocked.Increment(ref completed);
                });

                context.Fork(async () =>
                {
                    await BraidProbe.HitAsync("b", DefaultCancellationToken);
                    _ = Interlocked.Increment(ref completed);
                });

                return Task.CompletedTask;
            },
            new BraidOptions { Iterations = 1, Seed = 12345 },
            DefaultCancellationToken);

        await AssertRunCompletesBeforeWatchdogAsync(
            runTask,
            "Auto-join after callback should complete both workers.");

        Assert.Equal(2, completed);
    }

    /// <summary>
    /// Verifies fork delegates that return null fail clearly.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ForkOperationReturningNullTaskFailsClearly()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(async () =>
        {
            await Braid.RunAsync(
                static async context =>
                {
                    context.Fork(static () => null!);
                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions { Iterations = 1, Seed = 12345 },
                DefaultCancellationToken);
        });

        var report = exception.ToString();
        Assert.Contains("A forked operation failed.", exception.Message, StringComparison.Ordinal);
        Assert.True(
            report.Contains("null", StringComparison.OrdinalIgnoreCase)
            || report.Contains("Fork operation", StringComparison.OrdinalIgnoreCase),
            $"Expected clear null-task messaging. Report:{Environment.NewLine}{report}");
    }

    /// <summary>
    /// Verifies many sequential probes per worker complete without permit corruption.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ManySequentialProbesPerWorkerCompleteDeterministically()
    {
        const int probeCount = 10;

        var runTask = Braid.RunAsync(
            static async context =>
            {
                for (var w = 0; w < 3; w++)
                {
                    var workerIndex = w;
                    context.Fork(async () =>
                    {
                        for (var i = 0; i < probeCount; i++)
                        {
                            await BraidProbe.HitAsync($"step-{workerIndex}-{i}", DefaultCancellationToken);
                        }
                    });
                }

                await context.JoinAsync(DefaultCancellationToken);
            },
            new BraidOptions { Iterations = 1, Seed = 4242 },
            DefaultCancellationToken);

        await AssertRunCompletesBeforeWatchdogAsync(
            runTask,
            "Many sequential probes should complete deterministically.");
    }

    private static async Task AssertRunCompletesBeforeWatchdogAsync(Task runTask, string failureMessage)
    {
        var watchdog = Task.Delay(TimeSpan.FromSeconds(2), DefaultCancellationToken);
        var winner = await Task.WhenAny(runTask, watchdog);

        if (winner != runTask)
        {
            Assert.Fail($"Braid run did not complete before watchdog timeout. {failureMessage}");
        }

        await runTask;
    }
}
