using TUnit.Assertions.Enums;

namespace Braid.Tests;

/// <summary>Covers exploration of tests in which the order of the workers changes which probes a worker hits.</summary>
public sealed class ExplorePathChangeTests : TestBase
{
    private const string RetryBug = "bug on the retry path";
    private static readonly TimeSpan HangTimeout = TimeSpan.FromSeconds(1);

    /// <summary>Verifies exploration finds a failure on the retry path of a compare-and-swap loop for every seed, with a token that reproduces it.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ExploreFindsFailureOnRetryPath(CancellationToken cancellationToken)
    {
        IReadOnlyList<ReplayStep> firstSteps = [];

        for (var seed = 0; seed < 10; seed++)
        {
            var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
                Runner.ExploreAsync(
                    new ExploreOptionsBuilder().WithSeed(seed).Build(),
                    braid => RunRetryLoopAsync(braid.WorkerAsync, braid.JoinAsync, cancellationToken),
                    cancellationToken));

            _ = await Assert.That(exception.InnerException!.Message).IsEqualTo(RetryBug);

            // The seed does not change the order of the schedules, so every seed stops at the same one.
            if (seed == 0)
                firstSteps = exception.Steps;
            else
                _ = await Assert.That(exception.Steps).IsEquivalentTo(firstSteps, CollectionOrdering.Matching);
        }

        var replayed = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                context => RunRetryLoopAsync(ForkWith(context), context.JoinAsync, cancellationToken),
                new RunOptions { Iterations = 1, Seed = 0, Schedule = ReplaySchedule.Replay(firstSteps) },
                cancellationToken));

        _ = await Assert.That(replayed.InnerException!.Message).IsEqualTo(RetryBug);
    }

    /// <summary>Verifies exploration runs both sides of a branch that the order of the workers decides, and passes when neither fails.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ExploreRunsBothSidesOfABranch(CancellationToken cancellationToken)
    {
        var runs = 0;
        var w1PassedSecond = false;
        var w2PassedSecond = false;

        // Each worker hits its second probe only if the other worker has finished, so no single run shows the probes of every schedule.
        await Runner.ExploreAsync(
            static options => options.WithSeed(1),
            async braid =>
            {
                runs++;
                var w1Done = false;
                var w2Done = false;
                await braid.WorkerAsync(
                    "w1",
                    async () =>
                    {
                        await Probe.HitAsync("a", cancellationToken);
                        if (w2Done)
                        {
                            await Probe.HitAsync("b", cancellationToken);
                            w1PassedSecond = true;
                        }

                        w1Done = true;
                    });
                await braid.WorkerAsync(
                    "w2",
                    async () =>
                    {
                        await Probe.HitAsync("x", cancellationToken);
                        if (w1Done)
                        {
                            await Probe.HitAsync("y", cancellationToken);
                            w2PassedSecond = true;
                        }

                        w2Done = true;
                    });
                await braid.JoinAsync(cancellationToken);
            },
            cancellationToken);

        _ = await Assert.That(w1PassedSecond).IsTrue();
        _ = await Assert.That(w2PassedSecond).IsTrue();

        // The six orders of the two starts and the two first probes. Each has one order left after them:
        // the worker that passes its first probe last hits its second one.
        _ = await Assert.That(runs).IsEqualTo(6);
    }

    /// <summary>Verifies exploration completes a test in which a worker hits a probe again and again until another worker has run.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ExploreCompletesWhenAWorkerPollsAtAProbe(CancellationToken cancellationToken)
    {
        var runs = 0;

        // A run that always released the same waiting worker would release only "poller" and never end.
        await Runner.ExploreAsync(
            static options => options.WithSeed(1).WithMaxSchedules(50),
            async braid =>
            {
                runs++;
                var ready = false;
                await braid.WorkerAsync(
                    "poller",
                    async () =>
                    {
                        while (!ready)
                            await Probe.HitAsync("poll", cancellationToken);
                    });
                await braid.WorkerAsync(
                    "setter",
                    async () =>
                    {
                        await Probe.HitAsync("set", cancellationToken);
                        ready = true;
                    });
                await braid.JoinAsync(cancellationToken);
            },
            cancellationToken);

        // The poller can poll any number of times before the setter runs, so the schedules end only at MaxSchedules.
        _ = await Assert.That(runs).IsEqualTo(50);
    }

    /// <summary>Verifies exploration completes a test in which each worker retries at a probe until the other one leaves a critical section.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ExploreCompletesWhenWorkersRetryForALock(CancellationToken cancellationToken)
    {
        var maxInside = 0;

        await Runner.ExploreAsync(
            static options => options.WithSeed(1).WithMaxSchedules(200),
            async braid =>
            {
                var locked = 0;
                var inside = 0;
                await braid.WorkerAsync("w1", EnterAsync);
                await braid.WorkerAsync("w2", EnterAsync);
                await braid.JoinAsync(cancellationToken);

                async Task EnterAsync()
                {
                    while (Interlocked.CompareExchange(ref locked, 1, 0) != 0)
                        await Probe.HitAsync("retry", cancellationToken);

                    maxInside = Math.Max(maxInside, ++inside);
                    await Probe.HitAsync("inside", cancellationToken);
                    inside--;
                    Volatile.Write(ref locked, 0);
                }
            },
            cancellationToken);

        _ = await Assert.That(maxInside).IsEqualTo(1);
    }

    /// <summary>Verifies exploration fails, instead of skipping schedules, when the test does not take the same steps under the same schedule.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task TestThatDoesNotRepeatFailsExploration(CancellationToken cancellationToken)
    {
        var runs = 0;

        // w1 hits probe "a" in the first run only, so a later run cannot repeat the step "hit w1 a" that the first run took.
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.ExploreAsync(
                static options => options.WithSeed(1),
                async braid =>
                {
                    var probe = ++runs == 1 ? "a" : "z";
                    await braid.WorkerAsync("w1", async () => await Probe.HitAsync(probe, cancellationToken));
                    await braid.WorkerAsync("w2", async () => await Probe.HitAsync("b", cancellationToken));
                    await braid.JoinAsync(cancellationToken);
                },
                cancellationToken));

        _ = await Assert.That(exception.FailureOrigin).IsEqualTo(RunFailureOrigin.Scheduler);
        _ = await Assert.That(exception.Message).StartsWith("The test did not repeat under the same schedule");
        _ = await Assert.That(exception.InnerException).IsTypeOf<RunException>();
        _ = await Assert.That(exception.InnerException!.Message).Contains("could not be satisfied: hit w1 at a; actual probe is z.");
        _ = await Assert.That(exception.Steps).IsEquivalentTo([ReplayStep.Start("w1"), ReplayStep.Hit("w1", "a")], CollectionOrdering.Matching);
    }

    /// <summary>Verifies exploration fails when a run hangs before it has taken every step that an earlier run took: the test did not repeat.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task HangWhileRepeatingStepsFailsExploration(CancellationToken cancellationToken)
    {
        var runs = 0;

        // From the second run on, w1 waits before its first probe for w2 to start, so the run hangs at "start w1" with the rest of its steps unused.
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.ExploreAsync(
                static options => options.WithSeed(1).WithTimeout(HangTimeout),
                async braid =>
                {
                    var waits = ++runs > 1;
                    using var release = new CancellationTokenSource();
                    var releaseToken = release.Token;
                    var w2Started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    await braid.WorkerAsync(
                        "w1",
                        async () =>
                        {
                            if (waits)
                                await w2Started.Task.WaitAsync(releaseToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

                            await Probe.HitAsync("a", cancellationToken);
                        });
                    await braid.WorkerAsync(
                        "w2",
                        async () =>
                        {
                            w2Started.SetResult();
                            await Probe.HitAsync("b", cancellationToken);
                        });
                    try
                    {
                        await braid.JoinAsync(cancellationToken);
                    }
                    finally
                    {
                        // Lets the hung worker end, so the failed run does not wait for it.
                        await release.CancelAsync();
                    }
                },
                cancellationToken));

        _ = await Assert.That(exception.Message).StartsWith("The test did not repeat under the same schedule");
        _ = await Assert.That(exception.InnerException).IsTypeOf<RunException>();
        _ = await Assert.That(exception.InnerException!.Message).Contains("A running worker did not reach a probe while other workers were parked");
        _ = await Assert.That(runs).IsEqualTo(2);
    }

    /// <summary>Verifies exploration fails with the timeout when every schedule hangs on a worker that waits for a parked one, so nothing was checked.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ExploreFailsWhenEveryScheduleHangs(CancellationToken cancellationToken)
    {
        var runs = 0;

        // Each worker waits, before its first probe, for the other one to start, so whichever starts first hangs.
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.ExploreAsync(
                static options => options.WithSeed(1).WithTimeout(HangTimeout),
                async braid =>
                {
                    runs++;
                    using var release = new CancellationTokenSource();
                    var releaseToken = release.Token;
                    var w1Started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var w2Started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    await braid.WorkerAsync("w1", () => StartAndWaitAsync(w1Started, w2Started, "a", releaseToken, cancellationToken));
                    await braid.WorkerAsync("w2", () => StartAndWaitAsync(w2Started, w1Started, "b", releaseToken, cancellationToken));
                    try
                    {
                        await braid.JoinAsync(cancellationToken);
                    }
                    finally
                    {
                        // Lets the hung worker end, so the failed run does not wait for it.
                        await release.CancelAsync();
                    }
                },
                cancellationToken));

        _ = await Assert.That(exception.FailureOrigin).IsEqualTo(RunFailureOrigin.Scheduler);
        _ = await Assert.That(exception.Message).Contains("A running worker did not reach a probe while other workers were parked");
        _ = await Assert.That(exception.Steps).IsEquivalentTo([ReplayStep.Start("w1")], CollectionOrdering.Matching);

        // One run for each worker that starts first.
        _ = await Assert.That(runs).IsEqualTo(2);
    }

    private static Func<string, Func<Task>, Task> ForkWith(RunContext context)
    {
        return (workerId, operation) =>
        {
            context.Fork(workerId, operation);
            return Task.CompletedTask;
        };
    }

    /// <summary>
    /// Runs two workers that each add one to a shared value with a compare-and-swap loop. A worker whose swap fails goes around the loop again,
    /// so it hits "start" and "swap" twice; it fails after its "done" probe.
    /// </summary>
    /// <param name="fork">Forks a worker.</param>
    /// <param name="join">Joins the workers.</param>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that completes when the scenario has run.</returns>
    /// <exception cref="InvalidOperationException">A worker went around the loop again.</exception>
    private static async Task RunRetryLoopAsync(Func<string, Func<Task>, Task> fork, Func<CancellationToken, Task> join, CancellationToken cancellationToken)
    {
        var value = 0;

        await fork("w1", AddOneAsync);
        await fork("w2", AddOneAsync);
        await join(cancellationToken);

        async Task AddOneAsync()
        {
            var retried = false;
            while (true)
            {
                await Probe.HitAsync("start", cancellationToken);
                var read = Volatile.Read(ref value);
                await Probe.HitAsync("swap", cancellationToken);
                if (Interlocked.CompareExchange(ref value, read + 1, read) == read)
                    break;

                retried = true;
            }

            await Probe.HitAsync("done", cancellationToken);
            if (retried)
                throw new InvalidOperationException(RetryBug);
        }
    }

    private static async Task StartAndWaitAsync(TaskCompletionSource started, TaskCompletionSource otherStarted, string probe, CancellationToken releaseToken, CancellationToken cancellationToken)
    {
        started.SetResult();
        await otherStarted.Task.WaitAsync(releaseToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        await Probe.HitAsync(probe, cancellationToken);
    }
}
