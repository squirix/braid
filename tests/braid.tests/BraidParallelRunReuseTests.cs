using TUnit.Assertions.Enums;

namespace Braid.Tests;

/// <summary>Covers parallelism isolation and safe reuse of options and schedule instances across runs.</summary>
public sealed class BraidParallelRunReuseTests : TestBase
{
    /// <summary>Verifies many concurrent runs do not share scheduler state.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the test.</returns>
    [Test]
    public Task ManyIndependentRunsDoNotShareState(CancellationToken cancellationToken)
    {
        var runs = new Task[20];
        for (var i = 0; i < runs.Length; i++)
            runs[i] = RunIndependentParallelScenarioAsync(10_000 + i, cancellationToken);

        return AssertCompletesBeforeWatchdogAsync(Task.WhenAll(runs), "Braid run did not complete before watchdog timeout.", TimeSpan.FromSeconds(15), cancellationToken: cancellationToken);
    }

    /// <summary>Verifies parallel scripted runs each follow their own schedule.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the test.</returns>
    [Test]
    public async Task ParallelScriptedRunsUseTheirOwnSchedules(CancellationToken cancellationToken)
    {
        var scheduleA = ReplaySchedule.Replay(new ReplayStep("worker-1", "ready"), new ReplayStep("worker-2", "ready"));
        var scheduleB = ReplaySchedule.Replay(new ReplayStep("worker-2", "ready"), new ReplayStep("worker-1", "ready"));

        var orderA = new List<string>();
        var orderB = new List<string>();

        var runA = Runner.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    await Probe.HitAsync("ready", cancellationToken);
                    orderA.Add("worker-1");
                });

                context.Fork(async () =>
                {
                    await Probe.HitAsync("ready", cancellationToken);
                    orderA.Add("worker-2");
                });

                await context.JoinAsync(cancellationToken);
            },
            new RunOptions { Iterations = 1, Seed = 101, Schedule = scheduleA },
            cancellationToken);

        var runB = Runner.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    await Probe.HitAsync("ready", cancellationToken);
                    orderB.Add("worker-1");
                });

                context.Fork(async () =>
                {
                    await Probe.HitAsync("ready", cancellationToken);
                    orderB.Add("worker-2");
                });

                await context.JoinAsync(cancellationToken);
            },
            new RunOptions { Iterations = 1, Seed = 202, Schedule = scheduleB },
            cancellationToken);

        var combined = Task.WhenAll(runA, runB);
        await AssertCompletesBeforeWatchdogAsync(combined, "Braid run did not complete before watchdog timeout.", TimeSpan.FromSeconds(2), cancellationToken: cancellationToken);

        _ = await Assert.That(orderA).IsEquivalentTo(["worker-1", "worker-2"], CollectionOrdering.Matching);
        _ = await Assert.That(orderB).IsEquivalentTo(["worker-2", "worker-1"], CollectionOrdering.Matching);
    }

    /// <summary>Verifies the same options instance can be reused across separate runs.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task SameBraidOptionsReusedAcrossRuns(CancellationToken cancellationToken)
    {
        var options = new RunOptions { Iterations = 1, Seed = 12345 };

        for (var pass = 0; pass < 2; pass++)
            await RunOptionsReusePassAsync(options, cancellationToken);
    }

    /// <summary>Verifies the same schedule instance can be reused across runs with identical ordering.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task SameBraidScheduleReusedAcrossRuns(CancellationToken cancellationToken)
    {
        var schedule = ReplaySchedule.Replay(new ReplayStep("worker-2", "ready"), new ReplayStep("worker-1", "ready"));

        for (var pass = 0; pass < 2; pass++)
            await RunScheduleReusePassAsync(schedule, pass, cancellationToken);
    }

    /// <summary>Verifies a scripted schedule is not consumed by a single run.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ScriptedScheduleIsNotConsumedByRun(CancellationToken cancellationToken)
    {
        var schedule = ReplaySchedule.Replay(new ReplayStep("worker-2", "ready"), new ReplayStep("worker-1", "ready"));

        _ = await Assert.That(await RunOnceAsync(111)).IsEquivalentTo(["worker-2", "worker-1"], CollectionOrdering.Matching);
        _ = await Assert.That(await RunOnceAsync(222)).IsEquivalentTo(["worker-2", "worker-1"], CollectionOrdering.Matching);
        return;

        async Task<List<string>> RunOnceAsync(int seed)
        {
            var order = new List<string>();
            await Runner.RunAsync(
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
                new RunOptions { Iterations = 1, Seed = seed, Schedule = schedule },
                cancellationToken);

            return order;
        }
    }

    private static Task RunIndependentParallelScenarioAsync(int seed, CancellationToken cancellationToken = default)
    {
        var local = new CompletionCounter();
        return Runner.RunAsync(
            async context =>
            {
                ForkHitReadyAndIncrement(context, local, cancellationToken);
                ForkHitReadyAndIncrement(context, local, cancellationToken);
                await context.JoinAsync(cancellationToken);
            },
            new RunOptions { Iterations = 1, Seed = seed },
            cancellationToken);
    }

    private static async Task RunOptionsReusePassAsync(RunOptions options, CancellationToken cancellationToken = default)
    {
        var value = new CompletionCounter();
        await Runner.RunAsync(
            async context =>
            {
                ForkHitReadyAndIncrement(context, value, cancellationToken);
                await context.JoinAsync(cancellationToken);
            },
            options,
            cancellationToken);

        _ = await Assert.That(value.Value).IsEqualTo(1);
    }

    private static async Task RunScheduleReusePassAsync(ReplaySchedule schedule, int pass, CancellationToken cancellationToken = default)
    {
        var order = new List<string>();
        await Runner.RunAsync(
            async context =>
            {
                ForkHitReadyAddWorker(context, order, "worker-1", cancellationToken);
                ForkHitReadyAddWorker(context, order, "worker-2", cancellationToken);
                await context.JoinAsync(cancellationToken);
            },
            new RunOptions { Iterations = 1, Seed = 999 + pass, Schedule = schedule },
            cancellationToken);

        _ = await Assert.That(order).IsEquivalentTo(["worker-2", "worker-1"], CollectionOrdering.Matching);
    }
}
