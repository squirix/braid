using TUnit.Assertions.Enums;

namespace Braid.Tests;

/// <summary>Covers the start of a worker as a scheduling point: the order of the code before the first probes is chosen like the order at probes.</summary>
public sealed class WorkerStartTests : TestBase
{
    private const string ReadBeforeWrite = "w2 read before w1 wrote";

    /// <summary>Verifies random runs start the workers in different orders for different seeds, and in the same order for one seed.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task RandomRunsStartWorkersInBothOrders(CancellationToken cancellationToken)
    {
        var seenBySeed = new List<int>();
        for (var seed = 1; seed <= 20; seed++)
        {
            var seen = await RunReadBeforeProbeAsync(new RunOptions { Iterations = 1, Seed = seed }, cancellationToken);
            _ = await Assert.That(await RunReadBeforeProbeAsync(new RunOptions { Iterations = 1, Seed = seed }, cancellationToken)).IsEqualTo(seen);
            seenBySeed.Add(seen);
        }

        _ = await Assert.That(seenBySeed).Contains(0);
        _ = await Assert.That(seenBySeed).Contains(1);
    }

    /// <summary>Verifies start steps decide which worker runs its code before the first probe first.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task StartStepsDecideWhoStartsFirst(CancellationToken cancellationToken)
    {
        var writerFirst = ReplaySchedule.Replay(ReplayStep.Start("w1"), ReplayStep.Start("w2"), ReplayStep.Hit("w1", "a"), ReplayStep.Hit("w2", "b"));
        var readerFirst = ReplaySchedule.Replay(ReplayStep.Start("w2"), ReplayStep.Start("w1"), ReplayStep.Hit("w1", "a"), ReplayStep.Hit("w2", "b"));

        _ = await Assert.That(await RunReadBeforeProbeAsync(new RunOptions { Iterations = 1, Seed = 1, Schedule = writerFirst }, cancellationToken)).IsEqualTo(1);
        _ = await Assert.That(await RunReadBeforeProbeAsync(new RunOptions { Iterations = 1, Seed = 1, Schedule = readerFirst }, cancellationToken)).IsEqualTo(0);
    }

    /// <summary>Verifies a schedule without start steps starts every worker before its first step, in fork order, as schedules did before start steps existed.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ScheduleWithoutStartsUsesForkOrder(CancellationToken cancellationToken)
    {
        var trace = string.Empty;
        var schedule = ReplaySchedule.Replay(ReplayStep.Hit("w2", "b"), ReplayStep.Hit("w1", "a"));

        await Runner.RunAsync(
            async context =>
            {
                context.Fork("w1", async () => await Probe.HitAsync("a", cancellationToken));
                context.Fork("w2", async () => await Probe.HitAsync("b", cancellationToken));
                await context.JoinAsync(cancellationToken);
                trace = string.Join(" | ", context.TraceSteps);
            },
            new RunOptions { Iterations = 1, Seed = 1, Schedule = schedule },
            cancellationToken);

        _ = await Assert.That(trace).IsEqualTo(
            "w1 forked | w2 forked | w1 released | w1 hit a | w2 released | w2 hit b | w2 released at b | w2 completed | w1 released at a | w1 completed");
    }

    /// <summary>Verifies a worker can start after another worker has passed its probes.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task WorkerCanStartAfterAnotherFinished(CancellationToken cancellationToken)
    {
        var trace = string.Empty;
        var schedule = ReplaySchedule.Replay(ReplayStep.Start("w2"), ReplayStep.Hit("w2", "b"), ReplayStep.Start("w1"), ReplayStep.Hit("w1", "a"));

        await Runner.RunAsync(
            async context =>
            {
                context.Fork("w1", async () => await Probe.HitAsync("a", cancellationToken));
                context.Fork("w2", async () => await Probe.HitAsync("b", cancellationToken));
                await context.JoinAsync(cancellationToken);
                trace = string.Join(" | ", context.TraceSteps);
            },
            new RunOptions { Iterations = 1, Seed = 1, Schedule = schedule },
            cancellationToken);

        _ = await Assert.That(trace).IsEqualTo(
            "w1 forked | w2 forked | w2 released | w2 hit b | w2 released at b | w2 completed | w1 released | w1 hit a | w1 released at a | w1 completed");
    }

    /// <summary>Verifies exploration finds a race in the code before the first probes, and its replay token reproduces it.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ExploreFindsRaceBeforeFirstProbe(CancellationToken cancellationToken)
    {
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.ExploreAsync(
                static options => options.WithSeed(1),
                async braid =>
                {
                    var shared = 0;
                    var seen = -1;
                    await braid.WorkerAsync(
                        "w1",
                        async () =>
                        {
                            shared = 1;
                            await Probe.HitAsync("a", cancellationToken);
                        });
                    await braid.WorkerAsync(
                        "w2",
                        async () =>
                        {
                            seen = shared;
                            await Probe.HitAsync("b", cancellationToken);
                        });
                    await braid.JoinAsync(cancellationToken);
                    if (seen == 0)
                        throw new InvalidOperationException(ReadBeforeWrite);
                },
                cancellationToken));

        _ = await Assert.That(exception.InnerException!.Message).IsEqualTo(ReadBeforeWrite);
        _ = await Assert.That(exception.Steps[0]).IsEqualTo(ReplayStep.Start("w2"));
        _ = await Assert.That(exception.TryGetReplayText(out var replayText, out var error)).IsTrue().Because(error!);
        _ = await Assert.That(exception.ToString()).Contains("1. Start w2");

        var replayed = await RunReadBeforeProbeAsync(new RunOptions { Iterations = 1, Seed = 1, Schedule = ReplaySchedule.Parse(replayText) }, cancellationToken);
        _ = await Assert.That(replayed).IsEqualTo(0);
    }

    /// <summary>Verifies exploration orders workers that hit no probe at all, by their starts.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ExploreOrdersWorkersWithoutProbes(CancellationToken cancellationToken)
    {
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.ExploreAsync(
                static options => options.WithSeed(1),
                async braid =>
                {
                    var shared = 0;
                    var seen = -1;
                    await braid.WorkerAsync(
                        "w1",
                        () =>
                        {
                            shared = 1;
                            return Task.CompletedTask;
                        });
                    await braid.WorkerAsync(
                        "w2",
                        () =>
                        {
                            seen = shared;
                            return Task.CompletedTask;
                        });
                    await braid.JoinAsync(cancellationToken);
                    if (seen == 0)
                        throw new InvalidOperationException(ReadBeforeWrite);
                },
                cancellationToken));

        _ = await Assert.That(exception.InnerException!.Message).IsEqualTo(ReadBeforeWrite);
        _ = await Assert.That(exception.Steps).IsEquivalentTo([ReplayStep.Start("w2"), ReplayStep.Start("w1")], CollectionOrdering.Matching);
    }

    /// <summary>Verifies exploration tries every order of the starts and the hits: six schedules for two workers with one probe each.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ExploreTriesEveryStartAndHitOrder(CancellationToken cancellationToken)
    {
        var runs = 0;

        await Runner.ExploreAsync(
            static options => options.WithSeed(1),
            async braid =>
            {
                runs++;
                await braid.WorkerAsync("w1", async () => await Probe.HitAsync("a", cancellationToken));
                await braid.WorkerAsync("w2", async () => await Probe.HitAsync("b", cancellationToken));
                await braid.JoinAsync(cancellationToken);
            },
            cancellationToken);

        // One discovery run, then the interleavings of "start w1, hit w1 a" with "start w2, hit w2 b".
        _ = await Assert.That(runs).IsEqualTo(7);
    }

    /// <summary>Verifies a start step for a worker that has already started fails the schedule at once.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task SecondStartOfAWorkerFailsTheSchedule(CancellationToken cancellationToken)
    {
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            RunTwoWorkersAsync(ReplaySchedule.Replay(ReplayStep.Start("w1"), ReplayStep.Start("w1")), cancellationToken));

        _ = await Assert.That(exception.FailureOrigin).IsEqualTo(RunFailureOrigin.Scheduler);
        _ = await Assert.That(exception.Message).StartsWith("Scripted schedule step 2 could not be satisfied: start w1; the worker has already started or was not forked.");
    }

    /// <summary>Verifies a start step for a worker that was not forked fails the schedule at once.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task StartOfUnknownWorkerFailsTheSchedule(CancellationToken cancellationToken)
    {
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            RunTwoWorkersAsync(ReplaySchedule.Replay(ReplayStep.Start("nobody")), cancellationToken));

        _ = await Assert.That(exception.FailureOrigin).IsEqualTo(RunFailureOrigin.Scheduler);
        _ = await Assert.That(exception.Message).StartsWith("Scripted schedule step 1 could not be satisfied: start nobody;");
    }

    /// <summary>Verifies a schedule that starts workers itself does not start the others: a worker it never starts leaves the schedule exhausted.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task WorkerTheScheduleNeverStartsStaysIdle(CancellationToken cancellationToken)
    {
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            RunTwoWorkersAsync(ReplaySchedule.Replay(ReplayStep.Start("w1"), ReplayStep.Hit("w1", "a")), cancellationToken));

        _ = await Assert.That(exception.Message).StartsWith("Scripted schedule was exhausted before all workers completed.");
        _ = await Assert.That(exception.SchedulerDiagnostics!.WaitingWorkers).Contains(new ProbeWaitDiagnostic("w2", ProbeWaitDiagnostic.NotStartedProbeName));
        _ = await Assert.That(exception.Traces).DoesNotContain("w2 released");
    }

    /// <summary>Verifies a hit step for a worker that the schedule has not started says so.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task HitForUnstartedWorkerNamesTheCause(CancellationToken cancellationToken)
    {
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            RunTwoWorkersAsync(ReplaySchedule.Replay(ReplayStep.Start("w2"), ReplayStep.Hit("w1", "a")), cancellationToken));

        _ = await Assert.That(exception.Message).StartsWith(
            "Scripted schedule step 2 could not be satisfied: hit w1 at a; the worker has not started. A schedule with start steps starts only the workers it names.");
    }

    private static Task RunTwoWorkersAsync(ReplaySchedule schedule, CancellationToken cancellationToken)
    {
        return Runner.RunAsync(
            async context =>
            {
                context.Fork("w1", async () => await Probe.HitAsync("a", cancellationToken));
                context.Fork("w2", async () => await Probe.HitAsync("b", cancellationToken));
                await context.JoinAsync(cancellationToken);
            },
            new RunOptions { Iterations = 1, Seed = 1, Schedule = schedule },
            cancellationToken);
    }

    /// <summary>Runs a writer and a reader that both touch the shared value before their first probe.</summary>
    /// <param name="options">The run options.</param>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>The value the reader saw: 1 when the writer started first, 0 when the reader did.</returns>
    private static async Task<int> RunReadBeforeProbeAsync(RunOptions options, CancellationToken cancellationToken)
    {
        var shared = 0;
        var seen = -1;

        await Runner.RunAsync(
            async context =>
            {
                context.Fork(
                    "w1",
                    async () =>
                    {
                        shared = 1;
                        await Probe.HitAsync("a", cancellationToken);
                    });
                context.Fork(
                    "w2",
                    async () =>
                    {
                        seen = shared;
                        await Probe.HitAsync("b", cancellationToken);
                    });
                await context.JoinAsync(cancellationToken);
            },
            options,
            cancellationToken);

        return seen;
    }
}
