using Xunit;

namespace Braid.Tests;

/// <summary>Covers scheduler scope and cancellation behavior of the braid scheduler and run reporting.</summary>
public sealed class BraidSchedulerScopeAndCancellationTests : TestBase
{
    /// <summary>Verifies callback-observed cancellation before forking surfaces operation canceled.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task CallbackObservedCancelBeforeForkCanceled()
    {
        using var cts = new CancellationTokenSource();

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await BraidRunner.RunAsync(
                async _ =>
                {
                    await cts.CancelAsync();
                    cts.Token.ThrowIfCancellationRequested();
                },
                cts.Token);
        });
    }

    /// <summary>Verifies canceling token after completion does not affect completed runs.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task CancelingTokenAfterCompletionNoEffect()
    {
        using var cts = new CancellationTokenSource();
        await BraidRunner.RunAsync(
            static async context =>
            {
                context.Fork(static async () => await BraidProbe.HitAsync("ready", DefaultCancellationToken));
                await context.JoinAsync(DefaultCancellationToken);
            },
            new BraidOptions { Iterations = 1, Seed = 4011 },
            cts.Token);

        await cts.CancelAsync();
        Assert.True(cts.Token.IsCancellationRequested);
        await BraidProbe.HitAsync("outside", DefaultCancellationToken);
    }

    /// <summary>Verifies external cancellation surfaces as operation canceled and not braid run exception.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ExternalCancellationNoRunException()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await BraidRunner.RunAsync(static _ => Task.CompletedTask, cts.Token));
    }

    /// <summary>Verifies large replay schedules can be created and reused.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task LargeReplayScheduleCanBeCreatedAndReused()
    {
        const int stepCount = 100;
        var steps = new BraidStep[stepCount];
        for (var stepIndex = 0; stepIndex < steps.Length; stepIndex++)
            steps[stepIndex] = new BraidStep("worker-1", "tick");

        var schedule = BraidSchedule.Replay(steps);
        Assert.Equal(stepCount, schedule.Steps.Count);

        for (var pass = 0; pass < 2; pass++)
        {
            await BraidRunner.RunAsync(
                static async context =>
                {
                    context.Fork(static async () =>
                    {
                        for (var i = 0; i < stepCount; i++)
                            await BraidProbe.HitAsync("tick", DefaultCancellationToken);
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

    /// <summary>Verifies many sequential failed runs do not leak scope.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ManySequentialFailedRunsDoNotLeakScope()
    {
        for (var i = 0; i < 50; i++)
            await RunFailedScopeLeakCheckAsync(i);
    }

    /// <summary>Verifies many sequential successful runs do not leak scope.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ManySequentialRunsCompleteNoLeak()
    {
        const int runCount = 100;
        for (var i = 0; i < runCount; i++)
        {
            await BraidRunner.RunAsync(
                static async context =>
                {
                    context.Fork(static async () => await BraidProbe.HitAsync("p", DefaultCancellationToken));
                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions { Iterations = 1, Seed = 5000 + i },
                DefaultCancellationToken);

            await BraidProbe.HitAsync($"outside-{i}", DefaultCancellationToken);
        }

        var probe = BraidProbe.HitAsync("outside-scope-intact", DefaultCancellationToken);
        Assert.True(probe.IsCompletedSuccessfully);
        await probe;
    }

    /// <summary>Verifies many sequential timeout runs do not leak scope.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ManySequentialTimedOutRunsDoNotLeakScope()
    {
        for (var i = 0; i < 10; i++)
            await RunTimeoutScopeLeakCheckAsync(i);
    }

    /// <summary>Verifies parallel failed runs do not mix trace entries.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ParallelFailedRunsDoNotMixTraceEntries()
    {
        var tasks = new Task[10];
        for (var runId = 0; runId < tasks.Length; runId++)
            tasks[runId] = RunIsolatedFailedRunAsync(runId);

        await Task.WhenAll(tasks);
        return;

        static async Task RunIsolatedFailedRunAsync(int runId)
        {
            var ownProbe = $"probe-run-{runId}";
            var exception = await Assert.ThrowsAsync<BraidRunException>(async () =>
            {
                await BraidRunner.RunAsync(
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
                    continue;

                Assert.DoesNotContain($"probe-run-{other}", report, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>Verifies parallel timeout runs do not corrupt scope.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ParallelTimeoutRunsDoNotCorruptScope()
    {
        var tasks = new Task[5];
        for (var runId = 0; runId < tasks.Length; runId++)
            tasks[runId] = RunIsolatedTimeoutRunAsync(runId);

        await Task.WhenAll(tasks);
        await BraidProbe.HitAsync("outside-after-parallel-timeouts", DefaultCancellationToken);
        return;

        static async Task RunIsolatedTimeoutRunAsync(int runId)
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                _ = await Assert.ThrowsAsync<BraidRunException>(async () =>
                {
                    await BraidRunner.RunAsync(
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
        }
    }

    /// <summary>Verifies probe name matching is ordinal and case-sensitive.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ProbeNameMatchingIsCaseSensitive()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await BraidRunner.RunAsync(
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

    /// <summary>Verifies worker id matching is ordinal and case-sensitive.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task WorkerIdMatchingIsCaseSensitive()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await BraidRunner.RunAsync(
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

    /// <summary>Verifies worker-local cancellation after probe is reported as worker failure.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task WorkerLocalCancelAfterProbeAsFailure()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await BraidRunner.RunAsync(
                static async context =>
                {
                    context.Fork(static async () =>
                    {
                        await BraidProbe.HitAsync("ready", DefaultCancellationToken);
                        using var localCts = new CancellationTokenSource();
                        await localCts.CancelAsync();
                        await Task.Delay(TimeSpan.FromMilliseconds(1), TimeProvider.System, localCts.Token);
                    });

                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions { Iterations = 1, Seed = 4012 },
                DefaultCancellationToken);
        });

        _ = Assert.IsAssignableFrom<OperationCanceledException>(exception.InnerException);
        Assert.Contains("ready", exception.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Verifies worker-local cancellation before first probe is reported with fork trace.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task WorkerLocalCancelBeforeProbeAsFailure()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await BraidRunner.RunAsync(
                static async context =>
                {
                    context.Fork(static async () =>
                    {
                        using var localCts = new CancellationTokenSource();
                        await localCts.CancelAsync();
                        await Task.Delay(TimeSpan.FromMilliseconds(1), TimeProvider.System, localCts.Token);
                    });

                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions { Iterations = 1, Seed = 4013 },
                DefaultCancellationToken);
        });

        var report = exception.ToString();
        Assert.Contains("worker-1 forked", report, StringComparison.Ordinal);
        _ = Assert.IsAssignableFrom<OperationCanceledException>(exception.InnerException);
        Assert.Contains("Trace:", report, StringComparison.Ordinal);
    }

    /// <summary>Verifies startup release is traced before first probe hit.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task WorkerStartupReleaseReportedFirstProbe()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await BraidRunner.RunAsync(
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

    private static int IndexOfContains(IReadOnlyList<string> trace, string contains)
    {
        for (var i = 0; i < trace.Count; i++)
            if (trace[i].Contains(contains, StringComparison.Ordinal))
                return i;

        return -1;
    }

    private static async Task RunFailedScopeLeakCheckAsync(int runIndex)
    {
        _ = await Assert.ThrowsAsync<BraidRunException>(async () =>
        {
            await BraidRunner.RunAsync(
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

    private static async Task RunTimeoutScopeLeakCheckAsync(int runIndex)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var runTask = Assert.ThrowsAsync<BraidRunException>(async () =>
            {
                await BraidRunner.RunAsync(
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

            AssertCompletesBeforeWatchdog(runTask, "Timed out run should complete with exception.", TimeSpan.FromSeconds(3), false);
        }
        finally
        {
            _ = gate.TrySetResult();
        }

        await BraidProbe.HitAsync($"outside-timeout-{runIndex}", DefaultCancellationToken);
    }

    private static void AssertAppearsBefore(IReadOnlyList<string> trace, string first, string second)
    {
        var firstIndex = IndexOfContains(trace, first);
        var secondIndex = IndexOfContains(trace, second);
        Assert.True(firstIndex >= 0, $"Could not find trace entry containing '{first}'.");
        Assert.True(secondIndex >= 0, $"Could not find trace entry containing '{second}'.");
        Assert.True(firstIndex < secondIndex, $"Expected '{first}' before '{second}', but got indexes {firstIndex} and {secondIndex}.");
    }
}
