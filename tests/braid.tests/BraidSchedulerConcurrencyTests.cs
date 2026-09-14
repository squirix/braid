using TUnit.Assertions.Enums;

namespace Braid.Tests;

/// <summary>Covers scheduler concurrency behavior of the braid scheduler and run reporting.</summary>
public sealed class BraidSchedulerConcurrencyTests : TestBase
{
    /// <summary>Verifies concurrent probe hits from the same worker fail clearly or serialize safely.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public Task ConcurrentProbesSameWorkerFailSerialize(CancellationToken cancellationToken)
    {
        return AssertConcurrentProbeRaceToleratesAsync(
            () => Runner.RunAsync(
                async context =>
                {
                    context.Fork(async () => await RunTwoThreadProbeRaceAsync("a", "b", cancellationToken));
                    await context.JoinAsync(cancellationToken);
                },
                new RunOptions { Iterations = 1, Seed = 12345 },
                cancellationToken),
            true);
    }

    /// <summary>Verifies many sequential probes per worker complete without permit corruption.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public Task ManySequentialProbesDeterministic(CancellationToken cancellationToken)
    {
        const int probeCount = 10;
        return AssertCompletesBeforeWatchdogAsync(
            () => Runner.RunAsync(
                async context =>
                {
                    for (var workerIndex = 0; workerIndex < 3; workerIndex++)
                        ForkWorkerSequentialProbes(context, workerIndex, probeCount, cancellationToken);

                    await context.JoinAsync(cancellationToken);
                },
                new RunOptions { Iterations = 1, Seed = 4242 },
                cancellationToken),
            "Many sequential probes should complete deterministically.",
            cancellationToken: cancellationToken);
    }

    /// <summary>Verifies concurrent independent runs do not share scheduler or schedule state.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ParallelRunsDoNotShareSchedulerState(CancellationToken cancellationToken)
    {
        var orderA = new List<string>();
        var orderB = new List<string>();

        await AssertCompletesBeforeWatchdogAsync(
            () => Task.WhenAll(
                RunOrderedWorkerReplayAsync(orderA, 111, new ReplayStep("worker-1", "ready"), new ReplayStep("worker-2", "ready"), cancellationToken),
                RunOrderedWorkerReplayAsync(orderB, 222, new ReplayStep("worker-2", "ready"), new ReplayStep("worker-1", "ready"), cancellationToken)),
            "Parallel independent runs should complete.",
            cancellationToken: cancellationToken);

        _ = await Assert.That(orderA).IsEquivalentTo(["worker-1", "worker-2"], CollectionOrdering.Matching);
        _ = await Assert.That(orderB).IsEquivalentTo(["worker-2", "worker-1"], CollectionOrdering.Matching);
    }

    /// <summary>Verifies a second probe from a child task while the worker waits at a probe fails clearly.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public Task ProbeFromChildTaskFailsOrSerializes(CancellationToken cancellationToken)
    {
        return AssertConcurrentProbeRaceToleratesAsync(() => Runner.RunAsync(
            async context =>
            {
                context.Fork(async () => await RunTwoThreadProbeRaceAsync("parent", "child", cancellationToken));
                await context.JoinAsync(cancellationToken);
            },
            new RunOptions { Iterations = 1, Seed = 12345 },
            cancellationToken));
    }

    /// <summary>Verifies HitAsync from the run callback without a current worker completes immediately (no current task).</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public Task ProbeHitInsideRunCompletesImmediately(CancellationToken cancellationToken)
    {
        return AssertCompletesBeforeWatchdogAsync(
            () => Runner.RunAsync(
                async context =>
                {
                    await Probe.HitAsync("callback-probe", cancellationToken);

                    context.Fork(async () => await Probe.HitAsync("worker-probe", cancellationToken));

                    await context.JoinAsync(cancellationToken);
                },
                new RunOptions { Iterations = 1, Seed = 12345 },
                cancellationToken),
            "Probe outside a forked worker should not deadlock.",
            cancellationToken: cancellationToken);
    }

    /// <summary>Verifies random scheduling eventually completes all workers across seeds.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task RandomSchedulingCompletesAllSeeds(CancellationToken cancellationToken)
    {
        for (var seed = 1; seed <= 50; seed++)
            await RunRandomSchedulingSeedScenarioAsync(seed, cancellationToken);
    }

    /// <summary>Verifies one scheduled options instance is safe across parallel runs.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ReusedScheduledOptionsSafeAcrossRuns(CancellationToken cancellationToken)
    {
        var options = new RunOptions
        {
            Iterations = 1,
            Seed = 777,
            Schedule = ReplaySchedule.Replay(new ReplayStep("worker-1", "ready"), new ReplayStep("worker-2", "ready")),
        };

        var runs = new Task[10];
        for (var runIndex = 0; runIndex < runs.Length; runIndex++)
            runs[runIndex] = RunReusedScheduleScenarioAsync(options, cancellationToken);

        await Task.WhenAll(runs);
        return;

        static async Task RunReusedScheduleScenarioAsync(RunOptions sharedOptions, CancellationToken cancellationToken)
        {
            var localOrder = new List<string>();
            await Runner.RunAsync(
                async context =>
                {
                    context.Fork(async () =>
                    {
                        await Probe.HitAsync("ready", cancellationToken);
                        localOrder.Add("worker-1");
                    });
                    context.Fork(async () =>
                    {
                        await Probe.HitAsync("ready", cancellationToken);
                        localOrder.Add("worker-2");
                    });
                    await context.JoinAsync(cancellationToken);
                },
                sharedOptions,
                cancellationToken);

            _ = await Assert.That(localOrder).IsEquivalentTo(["worker-1", "worker-2"], CollectionOrdering.Matching);
        }
    }

    /// <summary>Verifies forked workers are stopped when the user callback throws before join.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task RunAsyncStopsForkedWorkersBeforeJoin(CancellationToken cancellationToken)
    {
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                context =>
                {
                    context.Fork(async () => await Probe.HitAsync("ready", cancellationToken));

                    throw new InvalidOperationException("callback failed");
                },
                new RunOptions { Iterations = 1, Seed = 12345 },
                cancellationToken));

        var report = exception.ToString();
        _ = await Assert.That(report).Contains("callback failed");
        _ = await Assert.That(report).Contains("worker-1 forked");
        _ = await Assert.That(report).Contains("Trace:");
    }

    private static Task RunOrderedWorkerReplayAsync(List<string> order, int seed, ReplayStep firstStep, ReplayStep secondStep, CancellationToken cancellationToken = default) => Runner.RunAsync(
        async context =>
        {
            context.Fork(async () =>
            {
                await Probe.HitAsync("ready", cancellationToken);
                order.Add("worker-1");
            });

            context.Fork(async () =>
            {
                await Probe.HitAsync("ready", cancellationToken);
                order.Add("worker-2");
            });

            await context.JoinAsync(cancellationToken);
        },
        new RunOptions
        {
            Iterations = 1,
            Seed = seed,
            Schedule = ReplaySchedule.Replay(firstStep, secondStep),
        },
        cancellationToken);

    private static async Task RunRandomSchedulingSeedScenarioAsync(int seed, CancellationToken cancellationToken = default)
    {
        var completed = new CompletionCounter();
        await Runner.RunAsync(
            async context =>
            {
                for (var workerIndex = 0; workerIndex < 5; workerIndex++)
                    ForkWorkerRandomProbes(context, workerIndex, completed, cancellationToken: cancellationToken);

                await context.JoinAsync(cancellationToken);
            },
            new RunOptions { Iterations = 1, Seed = seed, Timeout = TimeSpan.FromSeconds(1) },
            cancellationToken);

        _ = await Assert.That(completed.Value == 5).IsTrue().Because($"Seed {seed} completed {completed.Value} of 5 workers.");
    }
}
