using System.Collections.Concurrent;
using Xunit;

namespace Braid.Tests;

/// <summary>Focused misuse and concurrency regression tests discovered during audit.</summary>
public sealed class BraidMisuseConcurrencyAuditTests : TestBase
{
    /// <summary>Verifies callback failures are not masked by non-cooperative workers during stop.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task CallbackFailureIsNotMaskedByNonCooperativeWorkerDuringStop()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var exceptionTask = Assert.ThrowsAsync<BraidRunException>(async () =>
            {
                await BraidRunner.RunAsync(
                    context =>
                    {
                        context.Fork(async () =>
                        {
                            await BraidProbe.HitAsync("ready", DefaultCancellationToken);
                            await gate.Task.WaitAsync(DefaultCancellationToken);
                        });

                        throw new InvalidOperationException("callback boom");
                    },
                    new BraidOptions { Iterations = 1, Seed = 5101 },
                    DefaultCancellationToken);
            });

            AssertCompletesBeforeWatchdog(exceptionTask, "Run should fail quickly with callback failure.", TimeSpan.FromSeconds(3), false);
            var exception = await exceptionTask;
            Assert.Contains("callback boom", exception.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            _ = gate.TrySetResult();
        }
    }

    /// <summary>Verifies concurrent fork calls before join assign unique workers and complete.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ConcurrentForkCallsBeforeJoinAssignUniqueWorkerIds()
    {
        var completed = new CompletionCounter();

        await BraidRunner.RunAsync(
            async context =>
            {
                var forks = new Task[20];
                for (var forkIndex = 0; forkIndex < forks.Length; forkIndex++)
                {
                    forks[forkIndex] = ScheduleConcurrentForkAsync(context, completed);
                }

                await Task.WhenAll(forks);
                await context.JoinAsync(DefaultCancellationToken);
            },
            new BraidOptions { Iterations = 1, Seed = 5502 },
            DefaultCancellationToken);

        Assert.Equal(20, completed.Value);
    }

    /// <summary>Verifies forking from external task during active join fails clearly.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ForkFromExternalTaskDuringJoinFailsClearly()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await BraidRunner.RunAsync(
                async context =>
                {
                    context.Fork(async () =>
                    {
                        await BraidProbe.HitAsync("ready", DefaultCancellationToken);
                        await gate.Task.WaitAsync(DefaultCancellationToken);
                    });

                    var joinTask = context.JoinAsync(DefaultCancellationToken);
                    await Task.Yield();

                    var forkException = await Record.ExceptionAsync(() => StartNewOnThreadPoolAsync(() => context.Fork(static () => Task.CompletedTask), DefaultCancellationToken));

                    Assert.NotNull(forkException);
                    Assert.True(forkException is InvalidOperationException or BraidRunException, $"Unexpected fork exception type: {forkException.GetType().FullName}");

                    _ = gate.TrySetResult();
                    await joinTask;
                },
                new BraidOptions { Iterations = 1, Seed = 5501, Timeout = TimeSpan.FromSeconds(2) },
                DefaultCancellationToken);
        }
        finally
        {
            _ = gate.TrySetResult();
        }
    }

    /// <summary>Verifies fork racing with join either succeeds consistently or fails clearly.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ForkRacingWithJoinFailsClearlyOrCompletesConsistently()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var completed = 0;
            await BraidRunner.RunAsync(
                async context =>
                {
                    context.Fork(async () =>
                    {
                        await BraidProbe.HitAsync("ready", DefaultCancellationToken);
                        _ = Interlocked.Increment(ref completed);
                        await gate.Task.WaitAsync(DefaultCancellationToken);
                    });

                    var joinTask = context.JoinAsync(DefaultCancellationToken);
                    var forkException = await Record.ExceptionAsync(() => StartNewOnThreadPoolAsync(
                        () =>
                        {
                            context.Fork(() =>
                            {
                                _ = Interlocked.Increment(ref completed);
                                return Task.CompletedTask;
                            });
                        },
                        DefaultCancellationToken));

                    _ = gate.TrySetResult();
                    await joinTask;

                    if (forkException is null)
                    {
                        Assert.True(completed is 1 or 2);
                        return;
                    }

                    Assert.True(forkException is InvalidOperationException or BraidRunException, $"Unexpected fork exception type: {forkException.GetType().FullName}");
                },
                new BraidOptions { Iterations = 1, Seed = 5503, Timeout = TimeSpan.FromSeconds(2) },
                DefaultCancellationToken);
        }
        finally
        {
            _ = gate.TrySetResult();
        }
    }

    /// <summary>Verifies repeated canceled worker-local probes do not leak scope or hang.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ManyCanceledWorkerLocalProbesDoNotLeakScopeOrHang()
    {
        for (var runIndex = 0; runIndex < 100; runIndex++)
        {
            await RunCanceledProbeLeakCheckAsync(runIndex);
        }
    }

    /// <summary>Verifies many probe-free workers complete successfully.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ManyProbeFreeWorkersComplete()
    {
        var completed = new CompletionCounter();
        await BraidRunner.RunAsync(
            async context =>
            {
                for (var workerIndex = 0; workerIndex < 200; workerIndex++)
                {
                    ForkIncrementCompleted(context, completed);
                }

                await context.JoinAsync(DefaultCancellationToken);
            },
            new BraidOptions { Iterations = 1, Seed = 5302, Timeout = TimeSpan.FromSeconds(2) },
            DefaultCancellationToken);

        Assert.Equal(200, completed.Value);
    }

    /// <summary>Verifies many synchronously failing workers do not hang join.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ManySynchronouslyFailingWorkersDoNotHangJoin()
    {
        var exceptionTask = Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await BraidRunner.RunAsync(
                static async context =>
                {
                    for (var workerIndex = 0; workerIndex < 20; workerIndex++)
                    {
                        ForkSyncFailWorker(context, workerIndex);
                    }

                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions { Iterations = 1, Seed = 5301 },
                DefaultCancellationToken);
        });

        AssertCompletesBeforeWatchdog(exceptionTask, "Join should fail quickly for many synchronous failures.", TimeSpan.FromSeconds(3), false);
        var exception = await exceptionTask;
        var report = exception.ToString();
        Assert.Contains("sync-fail-", report, StringComparison.Ordinal);
        var forkedTraceCount = 0;
        for (var traceIndex = 0; traceIndex < exception.Trace.Count; traceIndex++)
        {
            if (exception.Trace[traceIndex].Contains("forked", StringComparison.Ordinal))
            {
                forkedTraceCount++;
            }
        }

        Assert.True(forkedTraceCount >= 20);
    }

    /// <summary>Verifies many workers at same probe can follow scripted reverse order.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ManyWorkersSameProbeScriptedOrderCompletes()
    {
        const int workerCount = 20;
        var releaseOrder = new ConcurrentQueue<string>();
        var steps = new BraidStep[workerCount];
        var stepIndex = 0;
        for (var workerIndex = workerCount; workerIndex >= 1; workerIndex--)
        {
            steps[stepIndex++] = new BraidStep($"worker-{workerIndex}", "ready");
        }

        await BraidRunner.RunAsync(
            async context =>
            {
                for (var workerIndex = 1; workerIndex <= workerCount; workerIndex++)
                {
                    ForkHitReadyForWorker(context, workerIndex, releaseOrder);
                }

                await context.JoinAsync(DefaultCancellationToken);
            },
            new BraidOptions
            {
                Iterations = 1,
                Seed = 5303,
                Schedule = BraidSchedule.Replay(steps),
            },
            DefaultCancellationToken);

        var expectedOrder = new List<string>(workerCount);
        for (var workerIndex = workerCount; workerIndex >= 1; workerIndex--)
        {
            expectedOrder.Add($"worker-{workerIndex}");
        }

        Assert.Equal(expectedOrder, releaseOrder);
    }

    /// <summary>Verifies multiple worker failures report one failure while trace still records both workers.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task MultipleWorkerFailuresReportOneFailureAndTraceAllWorkers()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await BraidRunner.RunAsync(
                static async context =>
                {
                    context.Fork(static async () =>
                    {
                        await BraidProbe.HitAsync("first", DefaultCancellationToken);
                        throw new InvalidOperationException("worker one failed");
                    });

                    context.Fork(static async () =>
                    {
                        await BraidProbe.HitAsync("second", DefaultCancellationToken);
                        throw new InvalidOperationException("worker two failed");
                    });

                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions
                {
                    Iterations = 1,
                    Seed = 5103,
                    Schedule = BraidSchedule.Replay(new BraidStep("worker-1", "first"), new BraidStep("worker-2", "second")),
                },
                DefaultCancellationToken);
        });

        var report = exception.ToString();
        Assert.True(
            report.Contains("worker one failed", StringComparison.Ordinal) || report.Contains("worker two failed", StringComparison.Ordinal),
            "Expected at least one worker failure message in report.");
        Assert.Contains("worker-1", report, StringComparison.Ordinal);
        Assert.Contains("worker-2", report, StringComparison.Ordinal);
    }

    /// <summary>Verifies callback canceled task with run token surfaces operation canceled.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncCallbackCanceledTaskWithRunTokenSurfacesOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await BraidRunner.RunAsync(
                async _ =>
                {
                    await cts.CancelAsync();
                    await Task.FromCanceled(cts.Token);
                },
                cts.Token);
        });
    }

    /// <summary>Verifies callback canceled task with unrelated token is treated as callback failure.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncCallbackCanceledTaskWithUnrelatedTokenIsReportedAsCallbackFailure()
    {
        var cancellationToken = new CancellationToken(true);
        var exception = await Assert.ThrowsAsync<BraidRunException>(async () => await BraidRunner.RunAsync(_ => Task.FromCanceled(cancellationToken), DefaultCancellationToken));

        Assert.True(exception.InnerException is OperationCanceledException);
        Assert.Contains("braid run failed.", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Verifies callback faulted task is surfaced as callback failure.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncCallbackFaultedTaskIsReported()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () => await BraidRunner.RunAsync(
            static _ => Task.FromException(new InvalidOperationException("callback faulted")),
            DefaultCancellationToken));

        Assert.Contains("callback faulted", exception.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Verifies random scheduling with same seed remains stable under parallel background noise.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task SameSeedRandomSchedulingStableUnderParallelNoise()
    {
        var first = await RunScenarioAsync();
        var second = await RunScenarioAsync();
        Assert.Equal(first, second);
        return;

        static async Task<string> RunScenarioAsync()
        {
            using var noiseCts = new CancellationTokenSource();
            var noiseToken = noiseCts.Token;

            var noiseTasks = new Task[4];
            for (var noiseIndex = 0; noiseIndex < noiseTasks.Length; noiseIndex++)
            {
                noiseTasks[noiseIndex] = StartNoiseYieldLoopAsync(noiseToken);
            }

            try
            {
                var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
                {
                    await BraidRunner.RunAsync(
                        static async context =>
                        {
                            context.Fork(static async () =>
                            {
                                await BraidProbe.HitAsync("a", DefaultCancellationToken);
                                await BraidProbe.HitAsync("a2", DefaultCancellationToken);
                            });

                            context.Fork(static async () =>
                            {
                                await BraidProbe.HitAsync("b", DefaultCancellationToken);
                                await BraidProbe.HitAsync("b2", DefaultCancellationToken);
                            });

                            await context.JoinAsync(DefaultCancellationToken);
                            throw new InvalidOperationException("forced-failure");
                        },
                        new BraidOptions { Iterations = 1, Seed = 5401 },
                        DefaultCancellationToken);
                });

                return exception.ToString().ReplaceLineEndings("\n");
            }
            finally
            {
                await noiseCts.CancelAsync();
                await Task.WhenAll(noiseTasks);
            }
        }
    }

    /// <summary>Verifies scripted scheduler waits for running worker to satisfy expected step.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ScheduleWaitsForRunningWorkerToReachExpectedProbe()
    {
        var probesHit = new CompletionCounter();
        var options = new BraidOptions
        {
            Iterations = 1,
            Seed = 5105,
            Schedule = BraidSchedule.Replay(new BraidStep("worker-1", "later"), new BraidStep("worker-2", "other")),
        };

        await BraidRunner.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    await Task.Yield();
                    await BraidProbe.HitAsync("later", DefaultCancellationToken);
                    _ = probesHit.Increment();
                });

                context.Fork(async () =>
                {
                    await BraidProbe.HitAsync("other", DefaultCancellationToken);
                    _ = probesHit.Increment();
                });

                await context.JoinAsync(DefaultCancellationToken);
            },
            options,
            DefaultCancellationToken);

        Assert.Equal(2, probesHit.Value);
        await BraidProbe.HitAsync("outside-schedule-wait", DefaultCancellationToken);
    }

    /// <summary>Verifies schedule cursor resets for each iteration.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ScriptedScheduleCursorResetsEachIteration()
    {
        var completed = new CompletionCounter();
        var options = new BraidOptions
        {
            Iterations = 2,
            Seed = 5403,
            Schedule = BraidSchedule.Replay(new BraidStep("worker-1", "ready")),
        };

        await BraidRunner.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    await BraidProbe.HitAsync("ready", DefaultCancellationToken);
                    _ = completed.Increment();
                });
                await context.JoinAsync(DefaultCancellationToken);
            },
            options,
            DefaultCancellationToken);

        Assert.Equal(2, completed.Value);
    }

    /// <summary>Verifies scripted schedules replay independently for each iteration.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ScriptedScheduleReplaysForEachIteration()
    {
        var completed = 0;
        await BraidRunner.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    await BraidProbe.HitAsync("ready", DefaultCancellationToken);
                    _ = Interlocked.Increment(ref completed);
                });
                await context.JoinAsync(DefaultCancellationToken);
            },
            new BraidOptions
            {
                Iterations = 3,
                Seed = 5402,
                Schedule = BraidSchedule.Replay(new BraidStep("worker-1", "ready")),
            },
            DefaultCancellationToken);

        Assert.Equal(3, completed);
    }

    /// <summary>Verifies synchronously completing worker trace includes fork/startup release/complete.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task SynchronouslyCompletingWorkerHasForkReleaseCompleteTrace()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await BraidRunner.RunAsync(
                static async context =>
                {
                    context.Fork(static () => Task.CompletedTask);
                    await context.JoinAsync(DefaultCancellationToken);
                    throw new InvalidOperationException("fail-after-join");
                },
                new BraidOptions { Iterations = 1, Seed = 5106 },
                DefaultCancellationToken);
        });

        var report = exception.ToString();
        Assert.Contains("worker-1 forked", report, StringComparison.Ordinal);
        Assert.Contains("worker-1 released", report, StringComparison.Ordinal);
        Assert.Contains("worker-1 completed", report, StringComparison.Ordinal);
        Assert.DoesNotContain("released at", report, StringComparison.Ordinal);
    }

    /// <summary>Verifies synchronously throwing fork delegates are reported clearly.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task SynchronouslyThrowingWorkerDelegateIsReported()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await BraidRunner.RunAsync(
                static async context =>
                {
                    context.Fork(static () => throw new InvalidOperationException("sync throw"));
                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions { Iterations = 1, Seed = 5107 },
                DefaultCancellationToken);
        });

        var report = exception.ToString();
        Assert.Contains("sync throw", report, StringComparison.Ordinal);
        Assert.Contains("worker-1 forked", report, StringComparison.Ordinal);
        Assert.Contains("worker-1 completed", report, StringComparison.Ordinal);
    }

    /// <summary>Verifies primary worker failures are not masked by non-cooperative sibling workers.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task WorkerFailureIsNotMaskedByNonCooperativeSiblingDuringStop()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var exceptionTask = Assert.ThrowsAsync<BraidRunException>(async () =>
            {
                await BraidRunner.RunAsync(
                    async context =>
                    {
                        context.Fork(static () => Task.FromException(new InvalidOperationException("primary worker failure")));
                        context.Fork(async () =>
                        {
                            await BraidProbe.HitAsync("waiter", DefaultCancellationToken);
                            await gate.Task.WaitAsync(DefaultCancellationToken);
                        });

                        await context.JoinAsync(DefaultCancellationToken);
                    },
                    new BraidOptions { Iterations = 1, Seed = 5102 },
                    DefaultCancellationToken);
            });

            AssertCompletesBeforeWatchdog(exceptionTask, "Worker failure should not be masked by stop path.", TimeSpan.FromSeconds(3), false);
            var exception = await exceptionTask;
            Assert.Contains("primary worker failure", exception.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            _ = gate.TrySetResult();
        }
    }

    /// <summary>Verifies worker failure while sibling waits at probe stops sibling cleanly.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task WorkerFailureWhileSiblingWaitsAtProbeStopsSiblingCleanly()
    {
        var exceptionTask = Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await BraidRunner.RunAsync(
                static async context =>
                {
                    context.Fork(static async () =>
                    {
                        await BraidProbe.HitAsync("fail-ready", DefaultCancellationToken);
                        throw new InvalidOperationException("failing worker");
                    });

                    context.Fork(static async () => await BraidProbe.HitAsync("blocked", DefaultCancellationToken));

                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions
                {
                    Iterations = 1,
                    Seed = 5104,
                    Schedule = BraidSchedule.Replay(new BraidStep("worker-1", "fail-ready"), new BraidStep("worker-2", "blocked")),
                },
                DefaultCancellationToken);
        });

        AssertCompletesBeforeWatchdog(exceptionTask, "Run should fail without deadlock.", TimeSpan.FromSeconds(3), false);
        var exception = await exceptionTask;
        var report = exception.ToString();
        Assert.Contains("failing worker", report, StringComparison.Ordinal);
        Assert.Contains("worker-2 hit blocked", report, StringComparison.Ordinal);
    }

    /// <summary>Verifies worker finally after timeout does not change the surfaced timeout failure.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task WorkerFinallyAfterTimeoutDoesNotThrowDisposedObjectException()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var workerFinallyObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            var exceptionTask = Assert.ThrowsAsync<BraidRunException>(async () =>
            {
                await BraidRunner.RunAsync(
                    async context =>
                    {
                        context.Fork(async () =>
                        {
                            try
                            {
                                await BraidProbe.HitAsync("ready", DefaultCancellationToken);
                                await gate.Task.WaitAsync(DefaultCancellationToken);
                            }
                            finally
                            {
                                _ = workerFinallyObserved.TrySetResult();
                            }
                        });

                        await context.JoinAsync(DefaultCancellationToken);
                    },
                    new BraidOptions { Iterations = 1, Seed = 5504, Timeout = TimeSpan.FromMilliseconds(50) },
                    DefaultCancellationToken);
            });

            AssertCompletesBeforeWatchdog(exceptionTask, "Timeout run should fail deterministically.", TimeSpan.FromSeconds(3), false);
            var exception = await exceptionTask;
            Assert.Contains("braid run timed out.", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            _ = gate.TrySetResult();
        }

        AssertCompletesBeforeWatchdog(workerFinallyObserved.Task, "Worker finally should complete after timeout.", TimeSpan.FromSeconds(3), false);
        await BraidProbe.HitAsync("outside-after-timeout", DefaultCancellationToken);
    }

    private static async Task RunCanceledProbeLeakCheckAsync(int runIndex)
    {
        _ = await Assert.ThrowsAsync<BraidRunException>(async () =>
        {
            await BraidRunner.RunAsync(
                static async context =>
                {
                    context.Fork(static () => BraidProbe.HitAsync("ready", new CancellationToken(true)).AsTask());
                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions { Iterations = 1, Seed = 5200 + runIndex },
                DefaultCancellationToken);
        });

        await BraidProbe.HitAsync($"outside-canceled-{runIndex}", DefaultCancellationToken);
    }
}
