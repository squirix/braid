namespace Braid.Tests;

/// <summary>Covers scheduler scope and cancellation behavior of the braid scheduler and run reporting.</summary>
public sealed class BraidSchedulerScopeAndCancellationTests : TestBase
{
    /// <summary>Verifies callback-observed cancellation before forking surfaces operation canceled.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task CallbackObservedCancelBeforeForkCanceled()
    {
        using var cts = new CancellationTokenSource();

        _ = await BraidAssertions.AssertExpectsAnyAsync<OperationCanceledException, CancellationTokenSource>(
            cts,
            static state => Runner.RunAsync(
                async _ =>
                {
                    await state.CancelAsync();
                    state.Token.ThrowIfCancellationRequested();
                },
                state.Token));
    }

    /// <summary>Verifies canceling token after completion does not affect completed runs.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task CancelingTokenAfterCompletionNoEffect(CancellationToken cancellationToken)
    {
        using var cts = new CancellationTokenSource();
        await Runner.RunAsync(
            async context =>
            {
                context.Fork(async () => await Probe.HitAsync("ready", cancellationToken));
                await context.JoinAsync(cancellationToken);
            },
            new RunOptions { Iterations = 1, Seed = 4011 },
            cts.Token);

        await cts.CancelAsync();
        _ = await Assert.That(cts.Token.IsCancellationRequested).IsTrue();
        await Probe.HitAsync("outside", cancellationToken);
    }

    /// <summary>Verifies external cancellation surfaces as operation-canceled and not braid run exception.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ExternalCancellationNoRunException()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        _ = await BraidAssertions.AssertExpectsAsync<OperationCanceledException, CancellationTokenSource>(cts, static state => Runner.RunAsync(static _ => Task.CompletedTask, state.Token));
    }

    /// <summary>Verifies large replay schedules can be created and reused.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task LargeReplayScheduleCanBeCreatedAndReused(CancellationToken cancellationToken)
    {
        const int stepCount = 100;
        var steps = new ReplayStep[stepCount];
        for (var stepIndex = 0; stepIndex < steps.Length; stepIndex++)
            steps[stepIndex] = new ReplayStep("worker-1", "tick");

        var schedule = ReplaySchedule.Replay(steps);
        _ = await Assert.That(schedule.Steps.Count).IsEqualTo(stepCount);

        for (var pass = 0; pass < 2; pass++)
        {
            await Runner.RunAsync(
                async context =>
                {
                    context.Fork(async () =>
                    {
                        for (var i = 0; i < stepCount; i++)
                            await Probe.HitAsync("tick", cancellationToken);
                    });

                    await context.JoinAsync(cancellationToken);
                },
                new RunOptions
                {
                    Iterations = 1,
                    Seed = 9014 + pass,
                    Schedule = schedule,
                },
                cancellationToken);
        }
    }

    /// <summary>Verifies many sequential failed runs do not leak scope.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ManySequentialFailedRunsDoNotLeakScope(CancellationToken cancellationToken)
    {
        for (var i = 0; i < 50; i++)
            await RunFailedScopeLeakCheckAsync(i, cancellationToken);
    }

    /// <summary>Verifies many sequential successful runs do not leak scope.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ManySequentialRunsCompleteNoLeak(CancellationToken cancellationToken)
    {
        const int runCount = 100;
        for (var i = 0; i < runCount; i++)
        {
            await Runner.RunAsync(
                async context =>
                {
                    context.Fork(async () => await Probe.HitAsync("p", cancellationToken));
                    await context.JoinAsync(cancellationToken);
                },
                new RunOptions { Iterations = 1, Seed = 5000 + i },
                cancellationToken);

            await Probe.HitAsync($"outside-{i}", cancellationToken);
        }

        var probe = Probe.HitAsync("outside-scope-intact", cancellationToken);
        _ = await Assert.That(probe.IsCompletedSuccessfully).IsTrue();
        await probe;
    }

    /// <summary>Verifies many sequential timeout runs do not leak scope.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ManySequentialTimedOutRunsDoNotLeakScope(CancellationToken cancellationToken)
    {
        for (var i = 0; i < 10; i++)
            await RunTimeoutScopeLeakCheckAsync(i, cancellationToken);
    }

    /// <summary>Verifies parallel failed runs do not mix trace entries.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ParallelFailedRunsDoNotMixTraceEntries(CancellationToken cancellationToken)
    {
        const int runCount = 10;
        var tasks = new Task[runCount];
        for (var runId = 0; runId < tasks.Length; runId++)
            tasks[runId] = RunIsolatedFailedRunAsync(runId, cancellationToken);

        await Task.WhenAll(tasks);
        return;

        static async Task RunIsolatedFailedRunAsync(int runId, CancellationToken cancellationToken)
        {
            var ownProbe = $"probe-run-{runId}";
            var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
                Runner.RunAsync(
                    async context =>
                    {
                        context.Fork(async () => await Probe.HitAsync(ownProbe, cancellationToken));
                        await context.JoinAsync(cancellationToken);
                        throw new InvalidOperationException($"run-{runId}-fail");
                    },
                    new RunOptions { Iterations = 1, Seed = 8000 + runId },
                    cancellationToken));

            var report = exception.ToString();
            _ = await Assert.That(report).Contains(ownProbe);
            for (var other = 0; other < runCount; other++)
            {
                if (other == runId)
                    continue;

                _ = await Assert.That(report).DoesNotContain($"probe-run-{other}");
            }
        }
    }

    /// <summary>Verifies parallel timeout runs do not corrupt scope.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ParallelTimeoutRunsDoNotCorruptScope(CancellationToken cancellationToken)
    {
        var tasks = new Task[5];
        for (var runId = 0; runId < tasks.Length; runId++)
            tasks[runId] = RunIsolatedTimeoutRunAsync(runId, cancellationToken);

        await Task.WhenAll(tasks);
        await Probe.HitAsync("outside-after-parallel-timeouts", cancellationToken);
        return;

        static async Task RunIsolatedTimeoutRunAsync(int runId, CancellationToken cancellationToken)
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                _ = await BraidAssertions.AssertExpectsAsync<RunException>(
                    Runner.RunAsync(
                        async context =>
                        {
                            context.Fork(async () =>
                            {
                                await Probe.HitAsync($"timeout-par-{runId}", cancellationToken);
                                await gate.Task.WaitAsync(cancellationToken);
                            });

                            await context.JoinAsync(cancellationToken);
                        },
                        new RunOptions { Iterations = 1, Seed = 9000 + runId, Timeout = TimeSpan.FromMilliseconds(50) },
                        cancellationToken));
            }
            finally
            {
                _ = gate.TrySetResult();
            }
        }
    }

    /// <summary>Verifies probe name matching is ordinal and case-sensitive.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ProbeNameMatchingIsCaseSensitive(CancellationToken cancellationToken)
    {
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                async context =>
                {
                    context.Fork(async () => await Probe.HitAsync("ready", cancellationToken));
                    await context.JoinAsync(cancellationToken);
                },
                new RunOptions
                {
                    Iterations = 1,
                    Seed = 9012,
                    Schedule = ReplaySchedule.Replay(new ReplayStep("worker-1", "READY")),
                },
                cancellationToken));

        var report = exception.ToString();
        _ = await Assert.That(report).Contains("READY");
        _ = await Assert.That(report).Contains("ready");
    }

    /// <summary>Verifies worker id matching is ordinal and case-sensitive.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task WorkerIdMatchingIsCaseSensitive(CancellationToken cancellationToken)
    {
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                async context =>
                {
                    context.Fork(async () => await Probe.HitAsync("ready", cancellationToken));
                    await context.JoinAsync(cancellationToken);
                },
                new RunOptions
                {
                    Iterations = 1,
                    Seed = 9011,
                    Schedule = ReplaySchedule.Replay(new ReplayStep("Worker-1", "ready")),
                },
                cancellationToken));

        var report = exception.ToString();
        _ = await Assert.That(report).Contains("Worker-1");
        _ = await Assert.That(report).Contains("worker-1 hit ready");
    }

    /// <summary>Verifies worker-local cancellation after probe is reported as worker failure.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task WorkerLocalCancelAfterProbeAsFailure(CancellationToken cancellationToken)
    {
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                async context =>
                {
                    context.Fork(async () =>
                    {
                        await Probe.HitAsync("ready", cancellationToken);
                        using var localCts = new CancellationTokenSource();
                        await localCts.CancelAsync();
                        await Task.Delay(TimeSpan.FromMilliseconds(1), TimeProvider.System, localCts.Token);
                    });

                    await context.JoinAsync(cancellationToken);
                },
                new RunOptions { Iterations = 1, Seed = 4012 },
                cancellationToken));

        _ = await Assert.That(exception.InnerException).IsTypeOf<OperationCanceledException>();
        _ = await Assert.That(exception.ToString()).Contains("ready");
    }

    /// <summary>Verifies worker-local cancellation before the first probe is reported with fork trace.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task WorkerLocalCancelBeforeProbeAsFailure(CancellationToken cancellationToken)
    {
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                async context =>
                {
                    context.Fork(static async () =>
                    {
                        using var localCts = new CancellationTokenSource();
                        await localCts.CancelAsync();
                        await Task.Delay(TimeSpan.FromMilliseconds(1), TimeProvider.System, localCts.Token);
                    });

                    await context.JoinAsync(cancellationToken);
                },
                new RunOptions { Iterations = 1, Seed = 4013 },
                cancellationToken));

        var report = exception.ToString();
        _ = await Assert.That(report).Contains("worker-1 forked");
        _ = await Assert.That(exception.InnerException).IsTypeOf<OperationCanceledException>();
        _ = await Assert.That(report).Contains("Trace:");
    }

    /// <summary>Verifies startup release is traced before the first probe hit.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task WorkerStartupReleaseReportedFirstProbe(CancellationToken cancellationToken)
    {
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                async context =>
                {
                    context.Fork(async () => await Probe.HitAsync("ready", cancellationToken));
                    await context.JoinAsync(cancellationToken);
                    throw new InvalidOperationException("fail-after-join");
                },
                new RunOptions { Iterations = 1, Seed = 4004 },
                cancellationToken));

        await AssertAppearsBeforeAsync(exception.Traces, "worker-1 forked", "worker-1 released");
        await AssertAppearsBeforeAsync(exception.Traces, "worker-1 released", "worker-1 hit ready");
    }

    private static async Task AssertAppearsBeforeAsync(IReadOnlyList<string> trace, string first, string second)
    {
        var firstIndex = IndexOfContains(trace, first);
        var secondIndex = IndexOfContains(trace, second);
        _ = await Assert.That(firstIndex >= 0).IsTrue().Because($"Could not find trace entry containing '{first}'.");
        _ = await Assert.That(secondIndex >= 0).IsTrue().Because($"Could not find trace entry containing '{second}'.");
        _ = await Assert.That(firstIndex < secondIndex).IsTrue().Because($"Expected '{first}' before '{second}', but got indexes {firstIndex} and {secondIndex}.");
    }

    private static int IndexOfContains(IReadOnlyList<string> trace, string contains)
    {
        for (var i = 0; i < trace.Count; i++)
        {
            if (trace[i].Contains(contains, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    private static async Task RunFailedScopeLeakCheckAsync(int runIndex, CancellationToken cancellationToken = default)
    {
        _ = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                async context =>
                {
                    context.Fork(async () => await Probe.HitAsync("p", cancellationToken));
                    await context.JoinAsync(cancellationToken);
                    throw new InvalidOperationException($"fail-{runIndex}");
                },
                new RunOptions { Iterations = 1, Seed = 6000 + runIndex },
                cancellationToken));

        await Probe.HitAsync($"outside-fail-{runIndex}", cancellationToken);
    }

    private static async Task RunTimeoutScopeLeakCheckAsync(int runIndex, CancellationToken cancellationToken = default)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var runTask = BraidAssertions.AssertExpectsAsync<RunException>(
                Runner.RunAsync(
                    async context =>
                    {
                        context.Fork(async () =>
                        {
                            await Probe.HitAsync($"timeout-{runIndex}", cancellationToken);
                            await gate.Task.WaitAsync(cancellationToken);
                        });

                        await context.JoinAsync(cancellationToken);
                    },
                    new RunOptions
                    {
                        Iterations = 1,
                        Seed = 7000 + runIndex,
                        Timeout = TimeSpan.FromMilliseconds(50),
                    },
                    cancellationToken));

            await AssertCompletesBeforeWatchdogAsync(runTask, "Timed out run should complete with exception.", TimeSpan.FromSeconds(3), false, cancellationToken);
        }
        finally
        {
            _ = gate.TrySetResult();
        }

        await Probe.HitAsync($"outside-timeout-{runIndex}", cancellationToken);
    }
}
