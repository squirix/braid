using Xunit;

namespace Braid.Tests;

/// <summary>Covers parallelism isolation and safe reuse of options and schedule instances across runs.</summary>
public sealed class BraidParallelRunReuseTests : TestBase
{
    /// <summary>Verifies many concurrent runs do not share scheduler state.</summary>
    /// <returns>A task that represents the test.</returns>
    [Fact]
    public Task ManyIndependentRunsDoNotShareState()
    {
        var runs = new Task[20];
        for (var i = 0; i < runs.Length; i++)
            runs[i] = RunIndependentParallelScenarioAsync(10_000 + i);

        return AssertCompletesBeforeWatchdogAsync(Task.WhenAll(runs), "Braid run did not complete before watchdog timeout.", TimeSpan.FromSeconds(15));
    }

    /// <summary>Verifies parallel scripted runs each follow their own schedule.</summary>
    /// <returns>A task that represents the test.</returns>
    [Fact]
    public async Task ParallelScriptedRunsUseTheirOwnSchedules()
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
                    await Probe.HitAsync("ready", DefaultCancellationToken);
                    orderA.Add("worker-1");
                });

                context.Fork(async () =>
                {
                    await Probe.HitAsync("ready", DefaultCancellationToken);
                    orderA.Add("worker-2");
                });

                await context.JoinAsync(DefaultCancellationToken);
            },
            new RunOptions { Iterations = 1, Seed = 101, Schedule = scheduleA },
            DefaultCancellationToken);

        var runB = Runner.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    await Probe.HitAsync("ready", DefaultCancellationToken);
                    orderB.Add("worker-1");
                });

                context.Fork(async () =>
                {
                    await Probe.HitAsync("ready", DefaultCancellationToken);
                    orderB.Add("worker-2");
                });

                await context.JoinAsync(DefaultCancellationToken);
            },
            new RunOptions { Iterations = 1, Seed = 202, Schedule = scheduleB },
            DefaultCancellationToken);

        var combined = Task.WhenAll(runA, runB);
        await AssertCompletesBeforeWatchdogAsync(combined, "Braid run did not complete before watchdog timeout.", TimeSpan.FromSeconds(2));

        Assert.Equal(["worker-1", "worker-2"], orderA);
        Assert.Equal(["worker-2", "worker-1"], orderB);
    }

    /// <summary>Verifies the same options instance can be reused across separate runs.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task SameBraidOptionsReusedAcrossRuns()
    {
        var options = new RunOptions { Iterations = 1, Seed = 12345 };

        for (var pass = 0; pass < 2; pass++)
            await RunOptionsReusePassAsync(options);
    }

    /// <summary>Verifies the same schedule instance can be reused across runs with identical ordering.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task SameBraidScheduleReusedAcrossRuns()
    {
        var schedule = ReplaySchedule.Replay(new ReplayStep("worker-2", "ready"), new ReplayStep("worker-1", "ready"));

        for (var pass = 0; pass < 2; pass++)
            await RunScheduleReusePassAsync(schedule, pass);
    }

    /// <summary>Verifies a scripted schedule is not consumed by a single run.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ScriptedScheduleIsNotConsumedByRun()
    {
        var schedule = ReplaySchedule.Replay(new ReplayStep("worker-2", "ready"), new ReplayStep("worker-1", "ready"));

        Assert.Equal(["worker-2", "worker-1"], await RunOnceAsync(111));
        Assert.Equal(["worker-2", "worker-1"], await RunOnceAsync(222));
        return;

        async Task<List<string>> RunOnceAsync(int seed)
        {
            var order = new List<string>();
            await Runner.RunAsync(
                async context =>
                {
                    context.Fork(async () =>
                    {
                        await Probe.HitAsync("ready", DefaultCancellationToken);
                        order.Add("worker-1");
                    });

                    context.Fork(async () =>
                    {
                        await Probe.HitAsync("ready", DefaultCancellationToken);
                        order.Add("worker-2");
                    });

                    await context.JoinAsync(DefaultCancellationToken);
                },
                new RunOptions { Iterations = 1, Seed = seed, Schedule = schedule },
                DefaultCancellationToken);

            return order;
        }
    }

    private static Task RunIndependentParallelScenarioAsync(int seed)
    {
        var local = new CompletionCounter();
        return Runner.RunAsync(
            async context =>
            {
                ForkHitReadyAndIncrement(context, local);
                ForkHitReadyAndIncrement(context, local);
                await context.JoinAsync(DefaultCancellationToken);
            },
            new RunOptions { Iterations = 1, Seed = seed },
            DefaultCancellationToken);
    }

    private static async Task RunOptionsReusePassAsync(RunOptions options)
    {
        var value = new CompletionCounter();
        await Runner.RunAsync(
            async context =>
            {
                ForkHitReadyAndIncrement(context, value);
                await context.JoinAsync(DefaultCancellationToken);
            },
            options,
            DefaultCancellationToken);

        Assert.Equal(1, value.Value);
    }

    private static async Task RunScheduleReusePassAsync(ReplaySchedule schedule, int pass)
    {
        var order = new List<string>();
        await Runner.RunAsync(
            async context =>
            {
                ForkHitReadyAddWorker(context, order, "worker-1");
                ForkHitReadyAddWorker(context, order, "worker-2");
                await context.JoinAsync(DefaultCancellationToken);
            },
            new RunOptions { Iterations = 1, Seed = 999 + pass, Schedule = schedule },
            DefaultCancellationToken);

        Assert.Equal(["worker-2", "worker-1"], order);
    }
}
