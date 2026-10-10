using TUnit.Assertions.Enums;

namespace Braid.Tests;

/// <summary>Covers how exploration handles worker starts: schedules cut before a start, the order of the first schedules, and start orders that hang.</summary>
public sealed class ExploreStartOrderTests : TestBase
{
    private const string LateStart = "w2 started after w1 passed its probe";

    /// <summary>Verifies a schedule cut before a worker started is completed with a start step for it, and the reported token reproduces the failure.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task CompletionStartsWorkerLeftUnstarted(CancellationToken cancellationToken)
    {
        // With one explored hit per schedule, the choices of "start w1; hit w1 a" end before w2 starts; the run then starts w2 itself.
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.ExploreAsync(
                static options => options.WithSeed(1).WithMaxStepsPerSchedule(1),
                braid => RunLateStartAsync(braid.WorkerAsync, braid.JoinAsync, cancellationToken),
                cancellationToken));

        _ = await Assert.That(exception.InnerException!.Message).IsEqualTo(LateStart);
        _ = await Assert.That(exception.Steps).IsEquivalentTo(
            [ReplayStep.Start("w1"), ReplayStep.Hit("w1", "a"), ReplayStep.Start("w2"), ReplayStep.Hit("w2", "b")],
            CollectionOrdering.Matching);
        _ = await Assert.That(exception.TryGetReplayText(out var replayText, out var error)).IsTrue().Because(error!);

        var replayed = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                context => RunLateStartAsync(ForkWith(context), context.JoinAsync, cancellationToken),
                new RunOptions { Iterations = 1, Seed = 1, Schedule = ReplaySchedule.Parse(replayText) },
                cancellationToken));

        _ = await Assert.That(replayed.InnerException!.Message).IsEqualTo(LateStart);
    }

    /// <summary>Verifies the first schedule starts the workers in fork order and then hits them in worker id order, as schedules did before start steps existed.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task FirstScheduleHitsWorkersInIdOrder(CancellationToken cancellationToken)
    {
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.ExploreAsync(
                static options => options.WithSeed(1),
                async braid =>
                {
                    await braid.WorkerAsync("z", async () => await Probe.HitAsync("pz", cancellationToken));
                    await braid.WorkerAsync("a", async () => await Probe.HitAsync("pa", cancellationToken));
                    await braid.JoinAsync(cancellationToken);
                    throw new InvalidOperationException("fails under every schedule");
                },
                cancellationToken));

        _ = await Assert.That(exception.Steps).IsEquivalentTo(
            [ReplayStep.Start("z"), ReplayStep.Start("a"), ReplayStep.Hit("a", "pa"), ReplayStep.Hit("z", "pz")],
            CollectionOrdering.Matching);
    }

    /// <summary>Verifies exploration runs one schedule, not all of them, from the group that begins with a start order that hangs.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task HangingStartOrderIsRunOnce(CancellationToken cancellationToken)
    {
        using var release = new CancellationTokenSource();
        var releaseToken = release.Token;
        var runs = 0;

        try
        {
            // w2 waits, before its first probe, for what w1 does before its own: every schedule that starts w2 first hangs.
            await Runner.ExploreAsync(
                static options => options.WithSeed(1).WithTimeout(TimeSpan.FromMilliseconds(300)),
                async braid =>
                {
                    _ = Interlocked.Increment(ref runs);
                    var w1Started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    await braid.WorkerAsync(
                        "w1",
                        async () =>
                        {
                            w1Started.SetResult();
                            await Probe.HitAsync("a", cancellationToken);
                        });
                    await braid.WorkerAsync(
                        "w2",
                        async () =>
                        {
                            await w1Started.Task.WaitAsync(releaseToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                            await Probe.HitAsync("b", cancellationToken);
                        });
                    await braid.JoinAsync(cancellationToken);
                },
                cancellationToken);
        }
        finally
        {
            await release.CancelAsync();
        }

        // The three schedules that start w1 first, and one of the three that start w2 first.
        _ = await Assert.That(runs).IsEqualTo(4);
    }

    private static Func<string, Func<Task>, Task> ForkWith(RunContext context)
    {
        return (workerId, operation) =>
        {
            context.Fork(workerId, operation);
            return Task.CompletedTask;
        };
    }

    /// <summary>Fails when w2 starts after w1 has passed its probe.</summary>
    /// <param name="fork">Forks a worker.</param>
    /// <param name="join">Joins the workers.</param>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that completes when the scenario has run.</returns>
    /// <exception cref="InvalidOperationException">w2 started after w1 passed its probe.</exception>
    private static async Task RunLateStartAsync(Func<string, Func<Task>, Task> fork, Func<CancellationToken, Task> join, CancellationToken cancellationToken)
    {
        var w1Passed = false;
        var startedLate = false;

        await fork(
            "w1",
            async () =>
            {
                await Probe.HitAsync("a", cancellationToken);
                w1Passed = true;
            });
        await fork(
            "w2",
            async () =>
            {
                startedLate = w1Passed;
                await Probe.HitAsync("b", cancellationToken);
            });
        await join(cancellationToken);

        if (startedLate)
            throw new InvalidOperationException(LateStart);
    }
}
