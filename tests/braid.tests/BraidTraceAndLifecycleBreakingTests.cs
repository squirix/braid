using Xunit;

namespace Braid.Tests;

/// <summary>
/// Deterministic breaking tests for trace correctness, lifecycle state, and report trustworthiness.
/// </summary>
public sealed class BraidTraceAndLifecycleBreakingTests : TestBase
{
    /// <summary>
    /// Verifies trace entries preserve expected fork/release/hit/complete relative order.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task TraceOrdersForkReleaseHitCompleteEvents()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await Braid.RunAsync(
                static async context =>
                {
                    context.Fork(static async () => await BraidProbe.HitAsync("ready", DefaultCancellationToken));
                    context.Fork(static async () => await BraidProbe.HitAsync("ready", DefaultCancellationToken));
                    await context.JoinAsync(DefaultCancellationToken);
                    throw new InvalidOperationException("fail-after-join");
                },
                new BraidOptions
                {
                    Iterations = 1,
                    Seed = 4001,
                    Schedule = BraidSchedule.Replay(
                        new BraidStep("worker-2", "ready"),
                        new BraidStep("worker-1", "ready")),
                },
                DefaultCancellationToken);
        });

        var trace = exception.Trace;
        AssertAppearsBefore(trace, "worker-1 forked", "worker-1 hit ready");
        AssertAppearsBefore(trace, "worker-2 forked", "worker-2 hit ready");
        AssertAppearsBefore(trace, "worker-2 released at ready", "worker-1 released at ready");
        AssertAppearsBefore(trace, "worker-1 released at ready", "worker-1 completed");
        AssertAppearsBefore(trace, "worker-2 released at ready", "worker-2 completed");
    }

    /// <summary>
    /// Verifies each worker completion appears exactly once in trace.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task TraceContainsSingleCompletionPerWorker()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await Braid.RunAsync(
                static async context =>
                {
                    context.Fork(static async () => await BraidProbe.HitAsync("ready", DefaultCancellationToken));
                    context.Fork(static async () => await BraidProbe.HitAsync("ready", DefaultCancellationToken));
                    await context.JoinAsync(DefaultCancellationToken);
                    throw new InvalidOperationException("fail-after-join");
                },
                new BraidOptions
                {
                    Iterations = 1,
                    Seed = 4002,
                    Schedule = BraidSchedule.Replay(
                        new BraidStep("worker-1", "ready"),
                        new BraidStep("worker-2", "ready")),
                },
                DefaultCancellationToken);
        });

        Assert.Equal(1, CountContains(exception.Trace, "worker-1 completed"));
        Assert.Equal(1, CountContains(exception.Trace, "worker-2 completed"));
    }

    /// <summary>
    /// Verifies probe release entries appear only after matching probe hits.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task TraceDoesNotReleaseProbeBeforeHit()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await Braid.RunAsync(
                static async context =>
                {
                    context.Fork(static async () => await BraidProbe.HitAsync("ready", DefaultCancellationToken));
                    await context.JoinAsync(DefaultCancellationToken);
                    throw new InvalidOperationException("fail-after-join");
                },
                new BraidOptions
                {
                    Iterations = 1,
                    Seed = 4003,
                    Schedule = BraidSchedule.Replay(new BraidStep("worker-1", "ready")),
                },
                DefaultCancellationToken);
        });

        AssertAppearsBefore(exception.Trace, "worker-1 hit ready", "worker-1 released at ready");
    }

    /// <summary>
    /// Verifies startup release is traced before first probe hit.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task WorkerStartupReleaseIsReportedBeforeFirstProbe()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await Braid.RunAsync(
                static async context =>
                {
                    context.Fork(static async () => await BraidProbe.HitAsync("ready", DefaultCancellationToken));
                    await context.JoinAsync(DefaultCancellationToken);
                    throw new InvalidOperationException("fail-after-join");
                },
                new BraidOptions { Iterations = 1, Seed = 4004 },
                DefaultCancellationToken);
        });

        AssertAppearsBefore(exception.Trace, "worker-1 forked", "worker-1 released");
        AssertAppearsBefore(exception.Trace, "worker-1 released", "worker-1 hit ready");
    }

    /// <summary>
    /// Verifies repeated probe hits generate repeated hit/release entries.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RepeatedSequentialProbeHitsProduceRepeatedTraceEntries()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await Braid.RunAsync(
                static async context =>
                {
                    context.Fork(static async () =>
                    {
                        await BraidProbe.HitAsync("loop", DefaultCancellationToken);
                        await BraidProbe.HitAsync("loop", DefaultCancellationToken);
                        await BraidProbe.HitAsync("loop", DefaultCancellationToken);
                    });

                    await context.JoinAsync(DefaultCancellationToken);
                    throw new InvalidOperationException("fail-after-join");
                },
                new BraidOptions
                {
                    Iterations = 1,
                    Seed = 4005,
                    Schedule = BraidSchedule.Replay(
                        new BraidStep("worker-1", "loop"),
                        new BraidStep("worker-1", "loop"),
                        new BraidStep("worker-1", "loop")),
                },
                DefaultCancellationToken);
        });

        Assert.Equal(3, CountContains(exception.Trace, "worker-1 hit loop"));
        Assert.Equal(3, CountContains(exception.Trace, "worker-1 released at loop"));
    }

    /// <summary>
    /// Verifies exception formatting succeeds for empty trace and empty schedule.
    /// </summary>
    [Fact]
    public void BraidRunExceptionToStringDoesNotThrowForEmptyTraceAndEmptySchedule()
    {
        var ex = new BraidRunException("message", 7, 3, [], [], null);
        var report = ex.ToString();
        Assert.Contains("message", report, StringComparison.Ordinal);
        Assert.Contains("Seed: 7", report, StringComparison.Ordinal);
        Assert.Contains("Iteration: 3", report, StringComparison.Ordinal);
        Assert.Contains("Trace:", report, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies exception formatting handles null inner exception deterministically.
    /// </summary>
    [Fact]
    public void BraidRunExceptionToStringHandlesNullInnerException()
    {
        var ex = new BraidRunException("message", 17, 1, ["worker-1 forked"], [new BraidStep("worker-1", "ready")], null);
        var first = ex.ToString();
        var second = ex.ToString();
        Assert.DoesNotContain("Inner exception:", first, StringComparison.Ordinal);
        Assert.Equal(first, second);
    }

    /// <summary>
    /// Verifies mismatch reports keep the failing schedule step and trace details.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ScheduleMismatchDoesNotAdvanceReplayCursorBeforeFailure()
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
                    Seed = 4006,
                    Schedule = BraidSchedule.Replay(new BraidStep("worker-1", "expected")),
                },
                DefaultCancellationToken);
        });

        var report = exception.ToString();
        Assert.Contains("Scripted schedule step 0", report, StringComparison.Ordinal);
        Assert.Contains("worker-1 @ expected", report, StringComparison.Ordinal);
        Assert.Contains("worker-1 hit actual", report, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies wrong probe diagnostics mention expected and actual probe.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task WrongProbeNameForCorrectWorkerReportsExpectedAndActualProbe()
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
                    Seed = 4007,
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
    /// Verifies wrong worker diagnostics mention expected worker and blocked actual worker.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task WrongWorkerForCorrectProbeReportsExpectedAndBlockedWorker()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await Braid.RunAsync(
                static async context =>
                {
                    context.Fork(static async () => await BraidProbe.HitAsync("ready", DefaultCancellationToken));
                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions
                {
                    Iterations = 1,
                    Seed = 4008,
                    Schedule = BraidSchedule.Replay(new BraidStep("worker-2", "ready")),
                },
                DefaultCancellationToken);
        });

        var report = exception.ToString();
        Assert.Contains("worker-2 @ ready", report, StringComparison.Ordinal);
        Assert.Contains("worker-1 hit ready", report, StringComparison.Ordinal);
        Assert.Contains("Scripted schedule step 0", report, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies callback failure without schedule does not print schedule section.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task CallbackFailureWithoutScheduleDoesNotPrintScheduleSection()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await Braid.RunAsync(static _ => throw new InvalidOperationException("callback-failed"), cancellationToken: DefaultCancellationToken);
        });

        var report = exception.ToString();
        Assert.Contains("Trace:", report, StringComparison.Ordinal);
        Assert.DoesNotContain("Schedule:", report, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies callback failure with schedule prints schedule section.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task CallbackFailureWithSchedulePrintsScheduleSection()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await Braid.RunAsync(
                static _ => throw new InvalidOperationException("callback-failed"),
                new BraidOptions
                {
                    Iterations = 1,
                    Seed = 4009,
                    Schedule = BraidSchedule.Replay(new BraidStep("worker-1", "ready")),
                },
                DefaultCancellationToken);
        });

        var report = exception.ToString();
        Assert.Contains("Schedule:", report, StringComparison.Ordinal);
        Assert.Contains("worker-1 @ ready", report, StringComparison.Ordinal);
        Assert.Contains("callback-failed", report, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies worker failures preserve original inner exception stack trace.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task WorkerFailurePreservesOriginalInnerExceptionStackTrace()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await Braid.RunAsync(
                static async context =>
                {
                    context.Fork(static () => Task.Run(ThrowFromWorkerHelper, DefaultCancellationToken));
                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions { Iterations = 1, Seed = 4010 },
                DefaultCancellationToken);
        });

        Assert.NotNull(exception.InnerException);
        Assert.Contains(nameof(ThrowFromWorkerHelper), exception.InnerException.StackTrace ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), exception.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies callback failures preserve original inner exception stack trace.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task CallbackFailurePreservesOriginalInnerExceptionStackTrace()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await Braid.RunAsync(static _ => ThrowFromCallbackHelper(), cancellationToken: DefaultCancellationToken);
        });

        Assert.NotNull(exception.InnerException);
        Assert.Contains(nameof(ThrowFromCallbackHelper), exception.InnerException.StackTrace ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies external cancellation surfaces as operation canceled and not braid run exception.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ExternalCancellationDoesNotCreateBraidRunException()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await Braid.RunAsync(static _ => Task.CompletedTask, cancellationToken: cts.Token);
        });
    }

    /// <summary>
    /// Verifies canceling token after completion does not affect completed runs.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task CancelingTokenAfterRunCompletionDoesNotAffectCompletedRun()
    {
        using var cts = new CancellationTokenSource();
        await Braid.RunAsync(
            static async context =>
            {
                context.Fork(static async () => await BraidProbe.HitAsync("ready", DefaultCancellationToken));
                await context.JoinAsync(DefaultCancellationToken);
            },
            new BraidOptions { Iterations = 1, Seed = 4011 },
            cts.Token);

        await cts.CancelAsync();
        await BraidProbe.HitAsync("outside", DefaultCancellationToken);
    }

    /// <summary>
    /// Verifies callback-observed cancellation before forking surfaces operation canceled.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task CallbackObservedCancellationBeforeForkSurfacesOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await Braid.RunAsync(
                _ =>
                {
                    cts.Cancel();
                    cts.Token.ThrowIfCancellationRequested();
                    return Task.CompletedTask;
                },
                cancellationToken: cts.Token);
        });
    }

    /// <summary>
    /// Verifies worker-local cancellation after probe is reported as worker failure.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task WorkerLocalCancellationAfterProbeIsReportedAsWorkerFailure()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await Braid.RunAsync(
                static async context =>
                {
                    context.Fork(static async () =>
                    {
                        await BraidProbe.HitAsync("ready", DefaultCancellationToken);
                        using var localCts = new CancellationTokenSource();
                        await localCts.CancelAsync();
                        await Task.Delay(TimeSpan.FromMilliseconds(1), localCts.Token);
                    });

                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions { Iterations = 1, Seed = 4012 },
                DefaultCancellationToken);
        });

        Assert.True(exception.InnerException is OperationCanceledException);
        Assert.Contains("ready", exception.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies worker-local cancellation before first probe is reported with fork trace.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task WorkerLocalCancellationBeforeFirstProbeIsReportedAsWorkerFailure()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await Braid.RunAsync(
                static async context =>
                {
                    context.Fork(static async () =>
                    {
                        using var localCts = new CancellationTokenSource();
                        await localCts.CancelAsync();
                        await Task.Delay(TimeSpan.FromMilliseconds(1), localCts.Token);
                    });

                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions { Iterations = 1, Seed = 4013 },
                DefaultCancellationToken);
        });

        var report = exception.ToString();
        Assert.Contains("worker-1 forked", report, StringComparison.Ordinal);
        Assert.True(exception.InnerException is OperationCanceledException);
        Assert.Contains("Trace:", report, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies many sequential successful runs do not leak scope.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ManySequentialRunsCompleteWithoutScopeLeak()
    {
        for (var i = 0; i < 100; i++)
        {
            await Braid.RunAsync(
                static async context =>
                {
                    context.Fork(static async () => await BraidProbe.HitAsync("p", DefaultCancellationToken));
                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions { Iterations = 1, Seed = 5000 + i },
                DefaultCancellationToken);

            await BraidProbe.HitAsync($"outside-{i}", DefaultCancellationToken);
        }
    }

    /// <summary>
    /// Verifies many sequential failed runs do not leak scope.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ManySequentialFailedRunsDoNotLeakScope()
    {
        for (var i = 0; i < 50; i++)
        {
            var runIndex = i;

            _ = await Assert.ThrowsAsync<BraidRunException>(async () =>
            {
                await Braid.RunAsync(
                    async context =>
                    {
                        context.Fork(static async () => await BraidProbe.HitAsync("p", DefaultCancellationToken));
                        await context.JoinAsync(DefaultCancellationToken);
                        throw new InvalidOperationException($"fail-{runIndex}");
                    },
                    new BraidOptions { Iterations = 1, Seed = 6000 + runIndex },
                    DefaultCancellationToken);
            });

            await BraidProbe.HitAsync($"outside-fail-{runIndex}", DefaultCancellationToken);
        }
    }

    /// <summary>
    /// Verifies many sequential timeout runs do not leak scope.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ManySequentialTimedOutRunsDoNotLeakScope()
    {
        for (var i = 0; i < 10; i++)
        {
            var runIndex = i;
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                var runTask = Assert.ThrowsAsync<BraidRunException>(async () =>
                {
                    await Braid.RunAsync(
                        async context =>
                        {
                            context.Fork(async () =>
                            {
                                await BraidProbe.HitAsync($"timeout-{runIndex}", DefaultCancellationToken);
                                await gate.Task.WaitAsync(DefaultCancellationToken);
                            });

                            await context.JoinAsync(DefaultCancellationToken);
                        },
                        new BraidOptions
                        {
                            Iterations = 1,
                            Seed = 7000 + runIndex,
                            Timeout = TimeSpan.FromMilliseconds(50),
                        },
                        DefaultCancellationToken);
                });

                await AssertCompletesBeforeWatchdogAsync(runTask, "Timed out run should complete with exception.");
            }
            finally
            {
                _ = gate.TrySetResult();
            }

            await BraidProbe.HitAsync($"outside-timeout-{runIndex}", DefaultCancellationToken);
        }
    }

    /// <summary>
    /// Verifies parallel failed runs do not mix trace entries.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ParallelFailedRunsDoNotMixTraceEntries()
    {
        var tasks = Enumerable.Range(0, 10).Select(static async runId =>
        {
            var ownProbe = $"probe-run-{runId}";
            var exception = await Assert.ThrowsAsync<BraidRunException>(async () =>
            {
                await Braid.RunAsync(
                    async context =>
                    {
                        context.Fork(async () => await BraidProbe.HitAsync(ownProbe, DefaultCancellationToken));
                        await context.JoinAsync(DefaultCancellationToken);
                        throw new InvalidOperationException($"run-{runId}-fail");
                    },
                    new BraidOptions { Iterations = 1, Seed = 8000 + runId },
                    DefaultCancellationToken);
            });

            var report = exception.ToString();
            Assert.Contains(ownProbe, report, StringComparison.Ordinal);
            for (var other = 0; other < 10; other++)
            {
                if (other == runId)
                {
                    continue;
                }

                Assert.DoesNotContain($"probe-run-{other}", report, StringComparison.Ordinal);
            }
        });

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Verifies parallel timeout runs do not corrupt scope.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ParallelTimeoutRunsDoNotCorruptScope()
    {
        var tasks = Enumerable.Range(0, 5).Select(async runId =>
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                _ = await Assert.ThrowsAsync<BraidRunException>(async () =>
                {
                    await Braid.RunAsync(
                        async context =>
                        {
                            context.Fork(async () =>
                            {
                                await BraidProbe.HitAsync($"timeout-par-{runId}", DefaultCancellationToken);
                                await gate.Task.WaitAsync(DefaultCancellationToken);
                            });

                            await context.JoinAsync(DefaultCancellationToken);
                        },
                        new BraidOptions { Iterations = 1, Seed = 9000 + runId, Timeout = TimeSpan.FromMilliseconds(50) },
                        DefaultCancellationToken);
                });
            }
            finally
            {
                _ = gate.TrySetResult();
            }
        });

        await Task.WhenAll(tasks);
        await BraidProbe.HitAsync("outside-after-parallel-timeouts", DefaultCancellationToken);
    }

    /// <summary>
    /// Verifies valid punctuation probe names are accepted and reported.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ProbeNameAllowsNonWhitespaceDiagnosticNames()
    {
        const string probeName = "phase:read/write#1";
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await Braid.RunAsync(
                static async context =>
                {
                    context.Fork(static async () => await BraidProbe.HitAsync(probeName, DefaultCancellationToken));
                    await context.JoinAsync(DefaultCancellationToken);
                    throw new InvalidOperationException("fail-after-join");
                },
                new BraidOptions
                {
                    Iterations = 1,
                    Seed = 9010,
                    Schedule = BraidSchedule.Replay(new BraidStep("worker-1", probeName)),
                },
                DefaultCancellationToken);
        });

        Assert.Contains(probeName, exception.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies worker id matching is ordinal and case-sensitive.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ScheduleWorkerIdMatchingIsOrdinalCaseSensitive()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await Braid.RunAsync(
                static async context =>
                {
                    context.Fork(static async () => await BraidProbe.HitAsync("ready", DefaultCancellationToken));
                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions
                {
                    Iterations = 1,
                    Seed = 9011,
                    Schedule = BraidSchedule.Replay(new BraidStep("Worker-1", "ready")),
                },
                DefaultCancellationToken);
        });

        var report = exception.ToString();
        Assert.Contains("Worker-1", report, StringComparison.Ordinal);
        Assert.Contains("worker-1 hit ready", report, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies probe name matching is ordinal and case-sensitive.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ScheduleProbeNameMatchingIsOrdinalCaseSensitive()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await Braid.RunAsync(
                static async context =>
                {
                    context.Fork(static async () => await BraidProbe.HitAsync("ready", DefaultCancellationToken));
                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions
                {
                    Iterations = 1,
                    Seed = 9012,
                    Schedule = BraidSchedule.Replay(new BraidStep("worker-1", "READY")),
                },
                DefaultCancellationToken);
        });

        var report = exception.ToString();
        Assert.Contains("READY", report, StringComparison.Ordinal);
        Assert.Contains("ready", report, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies long probe names can be reported without crashes.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task LongProbeNameIsReportedWithoutCrashing()
    {
        var probeName = new string('x', 512);
        var exception = await Assert.ThrowsAsync<BraidRunException>(async () =>
        {
            await Braid.RunAsync(
                async context =>
                {
                    context.Fork(async () => await BraidProbe.HitAsync(probeName, DefaultCancellationToken));
                    await context.JoinAsync(DefaultCancellationToken);
                    throw new InvalidOperationException("fail-after-join");
                },
                new BraidOptions
                {
                    Iterations = 1,
                    Seed = 9013,
                    Schedule = BraidSchedule.Replay(new BraidStep("worker-1", probeName)),
                },
                DefaultCancellationToken);
        });

        Assert.Contains(probeName, exception.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies large replay schedules can be created and reused.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task LargeReplayScheduleCanBeCreatedAndReused()
    {
        var steps = Enumerable.Range(0, 100)
            .Select(static _ => new BraidStep("worker-1", "tick"))
            .ToArray();
        var schedule = BraidSchedule.Replay(steps);

        for (var pass = 0; pass < 2; pass++)
        {
            await Braid.RunAsync(
                static async context =>
                {
                    context.Fork(static async () =>
                    {
                        for (var i = 0; i < 100; i++)
                        {
                            await BraidProbe.HitAsync("tick", DefaultCancellationToken);
                        }
                    });

                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions
                {
                    Iterations = 1,
                    Seed = 9014 + pass,
                    Schedule = schedule,
                },
                DefaultCancellationToken);
        }
    }

    /// <summary>
    /// Verifies empty replay schedule failure does not print schedule entries.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task EmptyReplayScheduleFailureDoesNotPrintScheduleEntries()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await Braid.RunAsync(
                static async context =>
                {
                    context.Fork(static () => Task.FromException(new InvalidOperationException("worker-failed")));
                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions
                {
                    Iterations = 1,
                    Seed = 9015,
                    Schedule = BraidSchedule.Replay(),
                },
                DefaultCancellationToken);
        });

        var report = exception.ToString();
        Assert.DoesNotContain("Schedule:", report, StringComparison.Ordinal);
        Assert.Contains("worker-failed", report, StringComparison.Ordinal);
    }

    private static int CountContains(IReadOnlyList<string> trace, string contains) => trace.Count(entry => entry.Contains(contains, StringComparison.Ordinal));

    private static void AssertAppearsBefore(IReadOnlyList<string> trace, string first, string second)
    {
        var firstIndex = IndexOfContains(trace, first);
        var secondIndex = IndexOfContains(trace, second);
        Assert.True(firstIndex >= 0, $"Could not find trace entry containing '{first}'.");
        Assert.True(secondIndex >= 0, $"Could not find trace entry containing '{second}'.");
        Assert.True(firstIndex < secondIndex, $"Expected '{first}' before '{second}', but got indexes {firstIndex} and {secondIndex}.");
    }

    private static int IndexOfContains(IReadOnlyList<string> trace, string contains)
    {
        for (var i = 0; i < trace.Count; i++)
        {
            if (trace[i].Contains(contains, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static Task ThrowFromCallbackHelper()
    {
        ThrowFromCallbackHelperCore();
        return Task.CompletedTask;
    }

    private static void ThrowFromCallbackHelperCore() => throw new InvalidOperationException("callback-helper-failure");

    private static void ThrowFromWorkerHelper() => ThrowFromWorkerHelperCore();

    private static void ThrowFromWorkerHelperCore() => throw new InvalidOperationException("worker-helper-failure");

    private static async Task AssertCompletesBeforeWatchdogAsync(Task task, string failureMessage)
    {
        var watchdog = Task.Delay(TimeSpan.FromSeconds(3), DefaultCancellationToken);
        if (await Task.WhenAny(task, watchdog) != task)
        {
            Assert.Fail(failureMessage);
        }

        await task;
    }
}
