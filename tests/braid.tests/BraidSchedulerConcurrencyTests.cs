using Xunit;

namespace Braid.Tests;

/// <summary>Covers schedulerconcurrency behavior of the braid scheduler and run reporting.</summary>
public sealed class BraidSchedulerConcurrencyTests : TestBase
{
    /// <summary>Verifies concurrent probe hits from the same worker fail clearly or serialize safely.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public Task ConcurrentProbesSameWorkerFailSerialize()
    {
        return AssertConcurrentProbeRaceFailureAsync(
            static () => BraidRunner.RunAsync(
                static async context =>
                {
                    context.Fork(static async () => await RunTwoThreadProbeRaceAsync("a", "b"));
                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions { Iterations = 1, Seed = 12345 },
                DefaultCancellationToken),
            true);
    }

    /// <summary>Verifies many sequential probes per worker complete without permit corruption.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public Task ManySequentialProbesDeterministic()
    {
        const int probeCount = 10;
        return AssertCompletesBeforeWatchdogAsync(
            static () => BraidRunner.RunAsync(
                static async context =>
                {
                    for (var workerIndex = 0; workerIndex < 3; workerIndex++)
                        ForkWorkerSequentialProbes(context, workerIndex, probeCount);

                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions { Iterations = 1, Seed = 4242 },
                DefaultCancellationToken),
            "Many sequential probes should complete deterministically.");
    }

    /// <summary>Verifies concurrent independent runs do not share scheduler or schedule state.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ParallelRunsDoNotShareSchedulerState()
    {
        var orderA = new List<string>();
        var orderB = new List<string>();

        await AssertCompletesBeforeWatchdogAsync(
            () => Task.WhenAll(
                RunOrderedWorkerReplayAsync(orderA, 111, new BraidStep("worker-1", "ready"), new BraidStep("worker-2", "ready")),
                RunOrderedWorkerReplayAsync(orderB, 222, new BraidStep("worker-2", "ready"), new BraidStep("worker-1", "ready"))),
            "Parallel independent runs should complete.");

        Assert.Equal(["worker-1", "worker-2"], orderA);
        Assert.Equal(["worker-2", "worker-1"], orderB);
    }

    /// <summary>Verifies a second probe from a child task while the worker waits at a probe fails clearly.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public Task ProbeFromChildTaskFailsOrSerializes()
    {
        return AssertConcurrentProbeRaceFailureAsync(static () => BraidRunner.RunAsync(
            static async context =>
            {
                context.Fork(static async () => await RunTwoThreadProbeRaceAsync("parent", "child"));
                await context.JoinAsync(DefaultCancellationToken);
            },
            new BraidOptions { Iterations = 1, Seed = 12345 },
            DefaultCancellationToken));
    }

    /// <summary>Verifies HitAsync from the run callback without a current worker completes immediately (no current task).</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public Task ProbeHitInsideRunCompletesImmediately()
    {
        return AssertCompletesBeforeWatchdogAsync(
            static () => BraidRunner.RunAsync(
                static async context =>
                {
                    await BraidProbe.HitAsync("callback-probe", DefaultCancellationToken);

                    context.Fork(static async () => await BraidProbe.HitAsync("worker-probe", DefaultCancellationToken));

                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions { Iterations = 1, Seed = 12345 },
                DefaultCancellationToken),
            "Probe outside a forked worker should not deadlock.");
    }

    /// <summary>Verifies random scheduling eventually completes all workers across seeds.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RandomSchedulingCompletesAllSeeds()
    {
        for (var seed = 1; seed <= 50; seed++)
            await RunRandomSchedulingSeedScenarioAsync(seed);
    }

    /// <summary>Verifies one scheduled options instance is safe across parallel runs.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ReusedScheduledOptionsSafeAcrossRuns()
    {
        var options = new BraidOptions
        {
            Iterations = 1,
            Seed = 777,
            Schedule = BraidSchedule.Replay(new BraidStep("worker-1", "ready"), new BraidStep("worker-2", "ready")),
        };

        var runs = new Task[10];
        for (var runIndex = 0; runIndex < runs.Length; runIndex++)
            runs[runIndex] = RunReusedScheduleScenarioAsync(options);

        await Task.WhenAll(runs);
        return;

        static async Task RunReusedScheduleScenarioAsync(BraidOptions sharedOptions)
        {
            var localOrder = new List<string>();
            await BraidRunner.RunAsync(
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
                sharedOptions,
                DefaultCancellationToken);

            Assert.Equal(["worker-1", "worker-2"], localOrder);
        }
    }

    /// <summary>Verifies forked workers are stopped when the user callback throws before join.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncStopsForkedWorkersBeforeJoin()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await BraidRunner.RunAsync(
                static context =>
                {
                    context.Fork(static async () => await BraidProbe.HitAsync("ready", DefaultCancellationToken));

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

    private static async Task RunRandomSchedulingSeedScenarioAsync(int seed)
    {
        var completed = new CompletionCounter();
        await BraidRunner.RunAsync(
            async context =>
            {
                for (var workerIndex = 0; workerIndex < 5; workerIndex++)
                    ForkWorkerRandomProbes(context, workerIndex, completed);

                await context.JoinAsync(DefaultCancellationToken);
            },
            new BraidOptions { Iterations = 1, Seed = seed, Timeout = TimeSpan.FromSeconds(1) },
            DefaultCancellationToken);

        Assert.Equal(5, completed.Value);
    }

    private static Task RunOrderedWorkerReplayAsync(List<string> order, int seed, BraidStep firstStep, BraidStep secondStep) => BraidRunner.RunAsync(
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
        new BraidOptions
        {
            Iterations = 1,
            Seed = seed,
            Schedule = BraidSchedule.Replay(firstStep, secondStep),
        },
        DefaultCancellationToken);
}
