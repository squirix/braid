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
                context.Fork(static async () => { await BraidProbe.HitAsync("ready", DefaultCancellationToken); });

                await context.JoinAsync(DefaultCancellationToken);
            },
            new BraidOptions { Iterations = 1, Seed = 12345 },
            DefaultCancellationToken);

        await AssertRunCompletesBeforeWatchdogAsync(runTask, "Double join should complete without SemaphoreFullException or BraidRunException.");
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
                context.Fork(static async () => { await BraidProbe.HitAsync("ready", DefaultCancellationToken); });

                await context.JoinAsync(DefaultCancellationToken);
                await context.JoinAsync(DefaultCancellationToken);
            },
            new BraidOptions { Iterations = 1, Seed = 12345 },
            DefaultCancellationToken);

        await AssertRunCompletesBeforeWatchdogAsync(runTask, "Sequential second JoinAsync should not deadlock or throw SemaphoreFullException.");
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

                context.Fork(static async () => { await BraidProbe.HitAsync("worker-probe", DefaultCancellationToken); });

                await context.JoinAsync(DefaultCancellationToken);
            },
            new BraidOptions { Iterations = 1, Seed = 12345 },
            DefaultCancellationToken);

        await AssertRunCompletesBeforeWatchdogAsync(runTask, "Probe outside a forked worker should not deadlock.");
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
            Schedule = BraidSchedule.Replay(new BraidStep("worker-1", "ready"), new BraidStep("worker-1", "ready")),
        };

        var exception = await Assert.ThrowsAsync<BraidRunException>(async () =>
        {
            await Braid.RunAsync(
                static async context =>
                {
                    context.Fork(static async () => { await BraidProbe.HitAsync("ready", DefaultCancellationToken); });

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
            Schedule = BraidSchedule.Replay(new BraidStep("worker-1", "ready"), new BraidStep("worker-2", "never")),
        };

        var exception = await Assert.ThrowsAsync<BraidRunException>(async () =>
        {
            await Braid.RunAsync(
                static async context =>
                {
                    context.Fork(static async () => { await BraidProbe.HitAsync("ready", DefaultCancellationToken); });

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
                context.Fork(static async () => { await BraidProbe.HitAsync("ready", DefaultCancellationToken); });

                context.Fork(static async () => { await BraidProbe.HitAsync("ready", DefaultCancellationToken); });

                var join1 = context.JoinAsync(DefaultCancellationToken);
                var join2 = context.JoinAsync(DefaultCancellationToken);
                await Task.WhenAll(join1, join2);
            },
            new BraidOptions { Iterations = 1, Seed = 12345 },
            DefaultCancellationToken);

        await AssertRunCompletesBeforeWatchdogAsync(runTask, "Concurrent JoinAsync should not deadlock or surface SemaphoreFullException.");
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
                context.Fork(static async () =>
                {
                    await BraidProbe.HitAsync("outer-before-nested", DefaultCancellationToken);
                    _ = await Assert.ThrowsAsync<InvalidOperationException>(static async () =>
                    {
                        await Braid.RunAsync(
                            static async inner =>
                            {
                                inner.Fork(static async () => { await BraidProbe.HitAsync("inner-ready", DefaultCancellationToken); });

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

        await AssertRunCompletesBeforeWatchdogAsync(runTask, "Nested run inside worker should fail clearly without corrupting outer run.");
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

                context.Fork(static async () => { await BraidProbe.HitAsync("ready", DefaultCancellationToken); });

                await context.JoinAsync(DefaultCancellationToken);
            },
            new BraidOptions { Iterations = 1, Seed = 12345 },
            DefaultCancellationToken);

        await AssertRunCompletesBeforeWatchdogAsync(runTask, "Nested run in callback should fail before corrupting outer scope.");
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
                Schedule = BraidSchedule.Replay(new BraidStep("worker-1", "ready"), new BraidStep("worker-2", "ready")),
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
                Schedule = BraidSchedule.Replay(new BraidStep("worker-2", "ready"), new BraidStep("worker-1", "ready")),
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
        var runTask = Braid.RunAsync(
            static async context =>
        {
            context.Fork(static async () =>
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
                            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(threadFailure).Throw();
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
    /// Verifies concurrent probe hits from the same worker fail clearly or serialize safely.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ConcurrentProbesFromSameWorkerFailClearlyOrSerialize()
    {
        var runTask = Braid.RunAsync(
            static async context =>
            {
                context.Fork(static async () =>
                {
                    var token = DefaultCancellationToken;
                    var ec = ExecutionContext.Capture() ?? throw new InvalidOperationException("ExecutionContext.Capture returned null.");
                    var readyCount = 0;
                    Exception? threadFailure = null;

                    await Task.Run(
                        () =>
                        {
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
            Assert.Contains("A forked operation failed.", exception.Message, StringComparison.Ordinal);
            Assert.Contains("Concurrent probe hit on the same worker is not supported.", exception.ToString(), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Verifies invalid probe names are rejected outside a braid run.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task HitAsyncRejectsInvalidProbeNameOutsideRun()
    {
        _ = await Assert.ThrowsAnyAsync<ArgumentException>(static () => BraidProbe.HitAsync(null!, DefaultCancellationToken).AsTask());
        _ = await Assert.ThrowsAnyAsync<ArgumentException>(static () => BraidProbe.HitAsync(string.Empty, DefaultCancellationToken).AsTask());
        _ = await Assert.ThrowsAnyAsync<ArgumentException>(static () => BraidProbe.HitAsync(" ", DefaultCancellationToken).AsTask());
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
                    _ = await Assert.ThrowsAnyAsync<ArgumentException>(static () => BraidProbe.HitAsync(null!, DefaultCancellationToken).AsTask());
                    _ = await Assert.ThrowsAnyAsync<ArgumentException>(static () => BraidProbe.HitAsync(string.Empty, DefaultCancellationToken).AsTask());
                    _ = await Assert.ThrowsAnyAsync<ArgumentException>(static () => BraidProbe.HitAsync(" ", DefaultCancellationToken).AsTask());
                    await BraidProbe.HitAsync("ok", DefaultCancellationToken);
                });

                await context.JoinAsync(DefaultCancellationToken);
            },
            new BraidOptions { Iterations = 1, Seed = 12345 },
            DefaultCancellationToken);

        await AssertRunCompletesBeforeWatchdogAsync(runTask, "Invalid probe names inside worker should throw ArgumentException without corrupting the run.");
    }

    /// <summary>
    /// Verifies a canceled probe token does not strand the worker in a permanent wait.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task CanceledProbeDoesNotLeaveWorkerPermanentlyWaiting()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await Braid.RunAsync(
                static async context =>
                {
                    context.Fork(static () => BraidProbe.HitAsync("ready", new CancellationToken(true)).AsTask());

                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions { Iterations = 1, Seed = 12345 },
                DefaultCancellationToken);
        });

        Assert.Contains("A forked operation failed.", exception.Message, StringComparison.Ordinal);
        Assert.NotNull(exception.InnerException);
        Assert.True(exception.InnerException is OperationCanceledException, $"Expected cancellation-derived exception, got {exception.InnerException.GetType().FullName}.");
    }

    /// <summary>
    /// Verifies cancellation at a probe preserves trace in the failure report.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task CanceledWorkerProbeFailureReportContainsProbeTrace()
    {
        var exceptionTask = Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await Braid.RunAsync(
                static async context =>
                {
                    context.Fork(static () => BraidProbe.HitAsync("ready", new CancellationToken(true)).AsTask());

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
        var runToken = runCts.Token;

        var runTask = Braid.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    await BraidProbe.HitAsync("block", DefaultCancellationToken);
                    while (!runToken.IsCancellationRequested)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(5), DefaultCancellationToken);
                    }

                    throw new InvalidOperationException("after-cancel worker failure");
                });

                await context.JoinAsync(runToken);
            },
            new BraidOptions { Iterations = 1, Seed = 12345 },
            runToken);

        await Task.Delay(TimeSpan.FromMilliseconds(50), DefaultCancellationToken);
        await runCts.CancelAsync();

        var oceTask = Assert.ThrowsAsync<OperationCanceledException>(() => runTask);
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
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await Braid.RunAsync(
                static context =>
                {
                    context.Fork(static async () => { await BraidProbe.HitAsync("ready", DefaultCancellationToken); });

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

        await AssertRunCompletesBeforeWatchdogAsync(runTask, "Auto-join after callback should complete both workers.");

        Assert.Equal(2, completed);
    }

    /// <summary>
    /// Verifies fork delegates that return null fail clearly.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ForkOperationReturningNullTaskFailsClearly()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
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
            report.Contains("null", StringComparison.OrdinalIgnoreCase) || report.Contains("Fork operation", StringComparison.OrdinalIgnoreCase),
            $"Expected clear null-task messaging. Report:{Environment.NewLine}{report}");
    }

    /// <summary>
    /// Verifies callback null-task failures are clearly reported.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncCallbackReturningNullTaskFailsClearly()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () => { await Braid.RunAsync(static _ => null!, cancellationToken: DefaultCancellationToken); });

        var report = exception.ToString();
        Assert.DoesNotContain(nameof(NullReferenceException), report, StringComparison.Ordinal);
        Assert.Contains("null", report, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("callback", report, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies context use after successful completion fails clearly.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ContextUseAfterSuccessfulRunFailsClearly()
    {
        BraidContext? capturedContext = null;
        await Braid.RunAsync(
            context =>
            {
                capturedContext = context;
                return Task.CompletedTask;
            },
            cancellationToken: DefaultCancellationToken);

        Assert.NotNull(capturedContext);
        var context = capturedContext;
        var forkException = Assert.ThrowsAny<Exception>(() => context.Fork(static () => Task.CompletedTask));
        Assert.True(
            forkException is InvalidOperationException or BraidRunException,
            $"Expected clear context-lifecycle failure. Got {forkException.GetType().FullName}: {forkException.Message}");

        var joinException = await Assert.ThrowsAnyAsync<Exception>(() => context.JoinAsync(DefaultCancellationToken));
        Assert.True(
            joinException is InvalidOperationException or BraidRunException,
            $"Expected clear context-lifecycle failure. Got {joinException.GetType().FullName}: {joinException.Message}");
    }

    /// <summary>
    /// Verifies context use after failed completion fails clearly.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ContextUseAfterFailedRunFailsClearly()
    {
        BraidContext? capturedContext = null;
        _ = await Assert.ThrowsAsync<BraidRunException>(async () =>
        {
            await Braid.RunAsync(
                context =>
                {
                    capturedContext = context;
                    throw new InvalidOperationException("callback failed");
                },
                cancellationToken: DefaultCancellationToken);
        });

        Assert.NotNull(capturedContext);
        var context = capturedContext;
        var forkException = Assert.ThrowsAny<Exception>(() => context.Fork(static () => Task.CompletedTask));
        Assert.True(
            forkException is InvalidOperationException or BraidRunException,
            $"Expected clear context-lifecycle failure. Got {forkException.GetType().FullName}: {forkException.Message}");

        var joinException = await Assert.ThrowsAnyAsync<Exception>(() => context.JoinAsync(DefaultCancellationToken));
        Assert.True(
            joinException is InvalidOperationException or BraidRunException,
            $"Expected clear context-lifecycle failure. Got {joinException.GetType().FullName}: {joinException.Message}");
    }

    /// <summary>
    /// Verifies context use after timeout fails clearly.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ContextUseAfterTimedOutRunFailsClearly()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        BraidContext? capturedContext = null;

        try
        {
            _ = await Assert.ThrowsAsync<BraidRunException>(async () =>
            {
                await Braid.RunAsync(
                    async context =>
                    {
                        capturedContext = context;
                        context.Fork(async () => await gate.Task.WaitAsync(DefaultCancellationToken));
                        await context.JoinAsync(DefaultCancellationToken);
                    },
                    new BraidOptions { Iterations = 1, Seed = 444, Timeout = TimeSpan.FromMilliseconds(50) },
                    DefaultCancellationToken);
            });
        }
        finally
        {
            _ = gate.TrySetResult();
        }

        Assert.NotNull(capturedContext);
        var context = capturedContext;
        var forkException = Assert.ThrowsAny<Exception>(() => context.Fork(static () => Task.CompletedTask));
        Assert.True(
            forkException is InvalidOperationException or BraidRunException,
            $"Expected clear context-lifecycle failure. Got {forkException.GetType().FullName}: {forkException.Message}");

        var joinException = await Assert.ThrowsAnyAsync<Exception>(() => context.JoinAsync(DefaultCancellationToken));
        Assert.True(
            joinException is InvalidOperationException or BraidRunException,
            $"Expected clear context-lifecycle failure. Got {joinException.GetType().FullName}: {joinException.Message}");
    }

    /// <summary>
    /// Verifies probe-free workers can complete without schedules.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task WorkerWithNoProbesCompletesWithoutSchedule()
    {
        var counter = 0;
        await Braid.RunAsync(
            context =>
            {
                context.Fork(() =>
                {
                    _ = Interlocked.Increment(ref counter);
                    return Task.CompletedTask;
                });

                return context.JoinAsync(DefaultCancellationToken);
            },
            new BraidOptions { Iterations = 1, Seed = 21 },
            DefaultCancellationToken);

        Assert.Equal(1, counter);
    }

    /// <summary>
    /// Verifies probe-free workers fail when replay steps are configured.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task WorkerWithNoProbesFailsWhenScheduleHasProbeSteps()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await Braid.RunAsync(
                static async context =>
                {
                    context.Fork(static () => Task.CompletedTask);
                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions
                {
                    Iterations = 1,
                    Seed = 22,
                    Schedule = BraidSchedule.Replay(new BraidStep("worker-1", "ready")),
                },
                DefaultCancellationToken);
        });

        var report = exception.ToString();
        Assert.Contains("unused steps", report, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("worker-1 completed", report, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies probe-free workers complete with empty replay schedules.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task WorkerWithNoProbesCompletesWithEmptyReplaySchedule()
    {
        await Braid.RunAsync(
            static async context =>
            {
                context.Fork(static () => Task.CompletedTask);
                await context.JoinAsync(DefaultCancellationToken);
            },
            new BraidOptions { Iterations = 1, Seed = 23, Schedule = BraidSchedule.Replay() },
            DefaultCancellationToken);
    }

    /// <summary>
    /// Verifies empty runs complete with empty replay schedules.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncCompletesWithNoWorkersAndEmptyReplaySchedule() => await Braid.RunAsync(
        static _ => Task.CompletedTask,
        new BraidOptions { Iterations = 1, Seed = 24, Schedule = BraidSchedule.Replay() },
        DefaultCancellationToken);

    /// <summary>
    /// Verifies empty runs fail with non-empty replay schedules.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncFailsWithNoWorkersAndNonEmptySchedule()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await Braid.RunAsync(
                static _ => Task.CompletedTask,
                new BraidOptions
                {
                    Iterations = 1,
                    Seed = 25,
                    Schedule = BraidSchedule.Replay(new BraidStep("worker-1", "ready")),
                },
                DefaultCancellationToken);
        });

        var report = exception.ToString();
        Assert.Contains("unused steps", report, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Schedule:", report, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies random scheduling eventually completes all workers across seeds.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RandomSchedulingCompletesAllWorkersAcrossManySeeds()
    {
        for (var seed = 1; seed <= 50; seed++)
        {
            var completed = 0;
            await Braid.RunAsync(
                async context =>
                {
                    for (var worker = 0; worker < 5; worker++)
                    {
                        var localWorker = worker;
                        context.Fork(async () =>
                        {
                            for (var probe = 0; probe < 5; probe++)
                            {
                                await BraidProbe.HitAsync($"w{localWorker}-p{probe}", DefaultCancellationToken);
                            }

                            _ = Interlocked.Increment(ref completed);
                        });
                    }

                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions { Iterations = 1, Seed = seed, Timeout = TimeSpan.FromSeconds(1) },
                DefaultCancellationToken);

            Assert.Equal(5, completed);
        }
    }

    /// <summary>
    /// Verifies deterministic seeds produce stable failure reports.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task SameSeedLargeScenarioProducesSameFailureReport()
    {
        var first = await RunOnceAsync();
        var second = await RunOnceAsync();
        Assert.Equal(first, second);
        return;

        static async Task<string> RunOnceAsync()
        {
            var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
            {
                await Braid.RunAsync(
                    static async context =>
                    {
                        for (var worker = 0; worker < 4; worker++)
                        {
                            var localWorker = worker;
                            context.Fork(async () =>
                            {
                                for (var probe = 0; probe < 4; probe++)
                                {
                                    await BraidProbe.HitAsync($"w{localWorker}-p{probe}", DefaultCancellationToken);
                                }
                            });
                        }

                        await context.JoinAsync(DefaultCancellationToken);
                        throw new InvalidOperationException("forced deterministic callback failure");
                    },
                    new BraidOptions { Iterations = 1, Seed = 31337 },
                    DefaultCancellationToken);
            });

            return exception.ToString().ReplaceLineEndings("\n");
        }
    }

    /// <summary>
    /// Verifies trace data does not leak across iterations.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task TraceDoesNotLeakAcrossIterations()
    {
        var calls = 0;
        var exception = await Assert.ThrowsAsync<BraidRunException>(async () =>
        {
            await Braid.RunAsync(
                async context =>
                {
                    var invocation = Interlocked.Increment(ref calls);
                    context.Fork(async () => await BraidProbe.HitAsync($"iteration-{invocation}", DefaultCancellationToken));
                    await context.JoinAsync(DefaultCancellationToken);
                    if (invocation == 3)
                    {
                        throw new InvalidOperationException("fail on third invocation");
                    }
                },
                new BraidOptions { Iterations = 3, Seed = 150 },
                DefaultCancellationToken);
        });

        var report = exception.ToString();
        Assert.Contains("iteration-3", report, StringComparison.Ordinal);
        Assert.DoesNotContain("iteration-1", report, StringComparison.Ordinal);
        Assert.DoesNotContain("iteration-2", report, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies callback failures before forking still report a trace section.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task CallbackFailureBeforeAnyForkReportsEmptyTraceSection()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await Braid.RunAsync(static _ => throw new InvalidOperationException("fail-before-fork"), new BraidOptions { Iterations = 1, Seed = 26 }, DefaultCancellationToken);
        });

        var report = exception.ToString();
        Assert.Contains("fail-before-fork", report, StringComparison.Ordinal);
        Assert.Contains("Trace:", report, StringComparison.Ordinal);
        Assert.DoesNotContain("worker-", report, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies callback failures after forking include fork trace entries.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task CallbackFailureAfterForkReportsForkedWorkerTrace()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await Braid.RunAsync(
                static context =>
                {
                    context.Fork(static async () => await BraidProbe.HitAsync("ready", DefaultCancellationToken));
                    throw new InvalidOperationException("fail-after-fork");
                },
                new BraidOptions { Iterations = 1, Seed = 27 },
                DefaultCancellationToken);
        });

        var report = exception.ToString();
        Assert.Contains("fail-after-fork", report, StringComparison.Ordinal);
        Assert.Contains("worker-1 forked", report, StringComparison.Ordinal);
        Assert.Contains("Trace:", report, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies schedule mismatch reports include blocked probe details.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ScheduleMismatchReportShowsBlockedWorkerProbe()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await Braid.RunAsync(
                static async context =>
                {
                    context.Fork(static async () => await BraidProbe.HitAsync("actual", DefaultCancellationToken));
                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions
                {
                    Iterations = 1,
                    Seed = 28,
                    Schedule = BraidSchedule.Replay(new BraidStep("worker-1", "expected")),
                },
                DefaultCancellationToken);
        });

        var report = exception.ToString();
        Assert.Contains("Scripted schedule step 0", report, StringComparison.Ordinal);
        Assert.Contains("worker-1", report, StringComparison.Ordinal);
        Assert.Contains("expected", report, StringComparison.Ordinal);
        Assert.Contains("actual", report, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies schedule mismatch traces include all blocked workers.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ScheduleMismatchReportShowsAllBlockedWorkersInTrace()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await Braid.RunAsync(
                static async context =>
                {
                    context.Fork(static async () => await BraidProbe.HitAsync("actual-a", DefaultCancellationToken));
                    context.Fork(static async () => await BraidProbe.HitAsync("actual-b", DefaultCancellationToken));
                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions
                {
                    Iterations = 1,
                    Seed = 29,
                    Schedule = BraidSchedule.Replay(new BraidStep("worker-1", "expected")),
                },
                DefaultCancellationToken);
        });

        var report = exception.ToString();
        Assert.Contains("worker-1 hit actual-a", report, StringComparison.Ordinal);
        Assert.Contains("worker-2 hit actual-b", report, StringComparison.Ordinal);
        Assert.Contains("expected", report, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies timeout reports include blocked worker trace entries.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task TimeoutReportIncludesBlockedWorkers()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var exception = await Assert.ThrowsAsync<BraidRunException>(async () =>
            {
                await Braid.RunAsync(
                    async context =>
                    {
                        context.Fork(async () =>
                        {
                            await BraidProbe.HitAsync("a", DefaultCancellationToken);
                            await gate.Task.WaitAsync(DefaultCancellationToken);
                        });
                        context.Fork(static async () => await BraidProbe.HitAsync("b", DefaultCancellationToken));
                        await context.JoinAsync(DefaultCancellationToken);
                    },
                    new BraidOptions { Iterations = 1, Seed = 30, Timeout = TimeSpan.FromMilliseconds(50) },
                    DefaultCancellationToken);
            });

            var report = exception.ToString();
            Assert.Contains("braid run timed out.", report, StringComparison.Ordinal);
            Assert.Contains("worker-1 hit a", report, StringComparison.Ordinal);
            Assert.Contains("worker-2 hit b", report, StringComparison.Ordinal);
        }
        finally
        {
            _ = gate.TrySetResult();
        }
    }

    /// <summary>
    /// Verifies timeout reports still include forked workers before probes.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task TimeoutBeforeAnyProbeStillReportsForkedWorkers()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var exception = await Assert.ThrowsAsync<BraidRunException>(async () =>
            {
                await Braid.RunAsync(
                    async context =>
                    {
                        context.Fork(async () => await gate.Task.WaitAsync(DefaultCancellationToken));
                        await context.JoinAsync(DefaultCancellationToken);
                    },
                    new BraidOptions { Iterations = 1, Seed = 31, Timeout = TimeSpan.FromMilliseconds(50) },
                    DefaultCancellationToken);
            });

            var report = exception.ToString();
            Assert.Contains("worker-1 forked", report, StringComparison.Ordinal);
            Assert.Contains("braid run timed out.", report, StringComparison.Ordinal);
        }
        finally
        {
            _ = gate.TrySetResult();
        }
    }

    /// <summary>
    /// Verifies exception properties are reflected in formatted reports.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task BraidRunExceptionPropertiesMatchFormattedReport()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await Braid.RunAsync(
                static async context =>
                {
                    context.Fork(static async () => await BraidProbe.HitAsync("actual", DefaultCancellationToken));
                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions
                {
                    Iterations = 1,
                    Seed = 32,
                    Schedule = BraidSchedule.Replay(new BraidStep("worker-1", "expected")),
                },
                DefaultCancellationToken);
        });

        var report = exception.ToString();
        Assert.Contains(exception.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture), report, StringComparison.Ordinal);
        Assert.Contains(exception.Iteration.ToString(System.Globalization.CultureInfo.InvariantCulture), report, StringComparison.Ordinal);
        foreach (var step in exception.Schedule)
        {
            Assert.Contains($"{step.WorkerId} @ {step.ProbeName}", report, StringComparison.Ordinal);
        }

        foreach (var traceEntry in exception.Trace)
        {
            Assert.Contains(traceEntry, report, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Verifies exception schedule snapshots are immutable for callers.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task BraidRunExceptionScheduleCannotBeMutated()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await Braid.RunAsync(
                static async context =>
                {
                    context.Fork(static async () => await BraidProbe.HitAsync("actual", DefaultCancellationToken));
                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions
                {
                    Iterations = 1,
                    Seed = 33,
                    Schedule = BraidSchedule.Replay(new BraidStep("worker-1", "expected")),
                },
                DefaultCancellationToken);
        });

        if (exception.Schedule is IList<BraidStep> list)
        {
            _ = Assert.ThrowsAny<Exception>(() => list[0] = new BraidStep("worker-9", "changed"));
        }

        Assert.Contains("worker-1 @ expected", exception.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies exception trace snapshots are immutable for callers.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task BraidRunExceptionTraceCannotBeMutated()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await Braid.RunAsync(
                static async context =>
                {
                    context.Fork(static async () => await BraidProbe.HitAsync("actual", DefaultCancellationToken));
                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions
                {
                    Iterations = 1,
                    Seed = 34,
                    Schedule = BraidSchedule.Replay(new BraidStep("worker-1", "expected")),
                },
                DefaultCancellationToken);
        });

        if (exception.Trace is IList<string> traceList)
        {
            _ = Assert.ThrowsAny<Exception>(() => traceList[0] = "mutated");
        }

        Assert.Contains("worker-1 hit actual", exception.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies shared default options are not mutated by runs.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task DefaultOptionsAreNotMutatedByRunAsync()
    {
        var beforeIterations = BraidOptions.Default.Iterations;
        var beforeSeed = BraidOptions.Default.Seed;
        var beforeTimeout = BraidOptions.Default.Timeout;
        var beforeSchedule = BraidOptions.Default.Schedule;

        await Braid.RunAsync(static _ => Task.CompletedTask, cancellationToken: DefaultCancellationToken);

        Assert.Equal(beforeIterations, BraidOptions.Default.Iterations);
        Assert.Equal(beforeSeed, BraidOptions.Default.Seed);
        Assert.Equal(beforeTimeout, BraidOptions.Default.Timeout);
        Assert.Same(beforeSchedule, BraidOptions.Default.Schedule);
    }

    /// <summary>
    /// Verifies one scheduled options instance is safe across parallel runs.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ReusedScheduledOptionsAreSafeAcrossParallelRuns()
    {
        var options = new BraidOptions
        {
            Iterations = 1,
            Seed = 777,
            Schedule = BraidSchedule.Replay(new BraidStep("worker-1", "ready"), new BraidStep("worker-2", "ready")),
        };

        var runs = Enumerable.Range(0, 10).Select(async _ =>
        {
            var localOrder = new List<string>();
            await Braid.RunAsync(
                async context =>
                {
                    context.Fork(async () =>
                    {
                        await BraidProbe.HitAsync("ready", DefaultCancellationToken);
                        localOrder.Add("worker-1");
                    });
                    context.Fork(async () =>
                    {
                        await BraidProbe.HitAsync("ready", DefaultCancellationToken);
                        localOrder.Add("worker-2");
                    });
                    await context.JoinAsync(DefaultCancellationToken);
                },
                options,
                DefaultCancellationToken);

            Assert.Equal(["worker-1", "worker-2"], localOrder);
        });

        await Task.WhenAll(runs);
    }

    /// <summary>
    /// Verifies tiny positive timeouts are valid and may deterministically time out.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task VerySmallPositiveTimeoutIsAllowedButMayTimeout()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var exception = await Assert.ThrowsAsync<BraidRunException>(async () =>
            {
                await Braid.RunAsync(
                    async context =>
                    {
                        context.Fork(async () => await gate.Task.WaitAsync(DefaultCancellationToken));
                        await context.JoinAsync(DefaultCancellationToken);
                    },
                    new BraidOptions { Iterations = 1, Seed = 35, Timeout = TimeSpan.FromTicks(1) },
                    DefaultCancellationToken);
            });

            Assert.Contains("timed out", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            _ = gate.TrySetResult();
        }
    }

    /// <summary>
    /// Verifies worker-local probe cancellation surfaces as worker failure.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ProbeCanceledByWorkerLocalTokenFailsAsWorkerFailure()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await Braid.RunAsync(
                static async context =>
                {
                    context.Fork(static () => BraidProbe.HitAsync("ready", new CancellationToken(true)).AsTask());
                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions { Iterations = 1, Seed = 36 },
                DefaultCancellationToken);
        });

        Assert.True(exception.InnerException is OperationCanceledException);
        Assert.Contains("ready", exception.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies run cancellation wins over worker-local probe cancellation.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunCancellationTakesPrecedenceOverWorkerLocalProbeCancellation()
    {
        using var runCts = new CancellationTokenSource();

        var runToken = runCts.Token;
        var workerForked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var runTask = Braid.RunAsync(
            async context =>
            {
                context.Fork(static () => BraidProbe.HitAsync("ready", new CancellationToken(true)).AsTask());

                workerForked.SetResult();
                await context.JoinAsync(runToken);
            },
            new BraidOptions
            {
                Iterations = 1,
                Seed = 37,
            },
            runToken);

        await workerForked.Task.WaitAsync(DefaultCancellationToken);
        await runCts.CancelAsync();

        var exception = await Record.ExceptionAsync(() => runTask);
        Assert.NotNull(exception);
        if (exception is OperationCanceledException)
        {
            return;
        }

        var runException = Assert.IsType<BraidRunException>(exception);
        Assert.True(runException.InnerException is OperationCanceledException);
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

        await AssertRunCompletesBeforeWatchdogAsync(runTask, "Many sequential probes should complete deterministically.");
    }

    /// <summary>
    /// Waits until both probe threads have reached the same point.
    /// </summary>
    /// <param name="readyCount">The shared ready counter.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    private static void WaitUntilBothThreadsAreReady(ref int readyCount, CancellationToken cancellationToken)
    {
        _ = Interlocked.Increment(ref readyCount);

        SpinWait spinWait = default;
        while (Volatile.Read(ref readyCount) < 2)
        {
            cancellationToken.ThrowIfCancellationRequested();
            spinWait.SpinOnce();
        }
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
