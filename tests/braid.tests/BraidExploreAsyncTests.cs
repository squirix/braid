using TUnit.Assertions.Enums;

namespace Braid.Tests;

/// <summary>Covers bounded schedule exploration.</summary>
public sealed class BraidExploreAsyncTests : TestBase
{
    /// <summary>Verifies exploration finds a lost update and exports a replay token.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ExploreAsyncFindsLostUpdateReplayToken(CancellationToken cancellationToken)
    {
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.ExploreAsync(
                static options => options.WithMaxSchedules(100).WithMaxStepsPerSchedule(10),
                braid => RunLostUpdateExploreAsync(braid, cancellationToken),
                cancellationToken));

        _ = await Assert.That(exception.TryGetReplayText(out var replayText, out var error)).IsTrue().Because(error!);
        _ = await Assert.That(replayText).Contains("after-read");
        _ = await Assert.That(replayText).Contains("before-write");

        var schedule = ReplaySchedule.Parse(replayText);
        _ = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                async context =>
                {
                    var value = 0;

                    context.Fork(
                        "worker-1",
                        async () =>
                        {
                            var current = value;
                            await Probe.HitAsync("after-read", cancellationToken);
                            await Probe.HitAsync("before-write", cancellationToken);
                            value = current + 1;
                        });

                    context.Fork(
                        "worker-2",
                        async () =>
                        {
                            var current = value;
                            await Probe.HitAsync("after-read", cancellationToken);
                            await Probe.HitAsync("before-write", cancellationToken);
                            value = current + 1;
                        });

                    await context.JoinAsync(cancellationToken);
                    _ = await Assert.That(value).IsEqualTo(2);
                },
                new RunOptions
                {
                    Iterations = 1,
                    Seed = 12_345,
                    Schedule = schedule,
                },
                cancellationToken));
    }

    /// <summary>Verifies the same bounds report the same first failure.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ExploreAsyncIsDeterministicForBounds(CancellationToken cancellationToken)
    {
        var first = await ExploreLostUpdateAsync(cancellationToken);
        var second = await ExploreLostUpdateAsync(cancellationToken);

        _ = await Assert.That(first.TryGetReplayText(out var firstToken, out var firstError)).IsTrue().Because(firstError!);
        _ = await Assert.That(second.TryGetReplayText(out var secondToken, out var secondError)).IsTrue().Because(secondError!);
        _ = await Assert.That(secondToken).IsEqualTo(firstToken);
    }

    /// <summary>Verifies exploration registers workers with stable ids.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ExploreAsyncRegistersStableWorkerIds(CancellationToken cancellationToken)
    {
        var first = await ExploreReaderWriterStableIdsAsync(cancellationToken);
        var second = await ExploreReaderWriterStableIdsAsync(cancellationToken);

        await AssertStableReaderWriterIdsAsync(first);
        await AssertStableReaderWriterIdsAsync(second);

        _ = await Assert.That(first.TryGetReplayText(out var firstReplay, out var firstError)).IsTrue().Because(firstError!);
        _ = await Assert.That(second.TryGetReplayText(out var secondReplay, out var secondError)).IsTrue().Because(secondError!);
        _ = await Assert.That(secondReplay).IsEqualTo(firstReplay);
    }

    /// <summary>Verifies named worker ids are used in generated replay schedules.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ExploreAsyncUsesNamedWorkerIdsReplay(CancellationToken cancellationToken)
    {
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.ExploreAsync(
                static options => options.WithMaxSchedules(100).WithMaxStepsPerSchedule(10),
                braid => RunNamedLostUpdateExploreAsync(braid, cancellationToken),
                cancellationToken));

        _ = await Assert.That(exception.TryGetReplayText(out var replayText, out var error)).IsTrue().Because(error!);
        _ = await Assert.That(replayText).Contains("reader");
        _ = await Assert.That(replayText).Contains("writer");
    }

    /// <summary>Verifies exploration completes when no assertion failure exists within bounds.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ExploreCompletesNoFailureWithinBounds(CancellationToken cancellationToken)
    {
        var explored = false;

        await Runner.ExploreAsync(
            static options => options.WithMaxSchedules(5).WithMaxStepsPerSchedule(4),
            async braid =>
            {
                await braid.WorkerAsync("worker-1", async () => await Probe.HitAsync("only-probe", cancellationToken));
                await braid.JoinAsync(cancellationToken);
                explored = true;
            },
            cancellationToken);

        _ = await Assert.That(explored).IsTrue();
    }

    /// <summary>Verifies a step cap below the schedule length still runs each schedule to completion.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ExploreFindsFailureWithSmallSteps(CancellationToken cancellationToken)
    {
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.ExploreAsync(
                static options => options.WithMaxSchedules(100).WithMaxStepsPerSchedule(3),
                braid => RunLostUpdateExploreAsync(braid, cancellationToken),
                cancellationToken));

        _ = await Assert.That(exception.FailureOrigin).IsEqualTo(RunFailureOrigin.UserTest);
        _ = await Assert.That(exception.TryGetReplayText(out _, out var error)).IsTrue().Because(error!);
        _ = await Assert.That(exception.Steps.Count).IsEqualTo(6);
    }

    /// <summary>Verifies an assertion after the join fails under a schedule cut by the step cap, and its replay token reproduces it.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ExploreFindsOrderFailureAfterStepCap(CancellationToken cancellationToken)
    {
        // The schedule starts both workers and explores "hit second b1; hit first a1"; the run then releases the workers in turn itself,
        // "second" first, so "second" finishes first.
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.ExploreAsync(
                static options => options.WithMaxSchedules(10).WithMaxStepsPerSchedule(2),
                braid => RunFinishOrderExploreAsync(braid, cancellationToken),
                cancellationToken));

        _ = await Assert.That(exception.FailureOrigin).IsEqualTo(RunFailureOrigin.UserTest);
        _ = await Assert.That(exception.TryGetReplayText(out var replayText, out var error)).IsTrue().Because(error!);
        _ = await Assert.That(exception.Steps).IsEquivalentTo(
            [
                ReplayStep.Start("first"),
                ReplayStep.Start("second"),
                ReplayStep.Hit("second", "b1"),
                ReplayStep.Hit("first", "a1"),
                ReplayStep.Hit("second", "b2"),
                ReplayStep.Hit("first", "a2"),
            ],
            CollectionOrdering.Matching);

        var replayed = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                context => RunFinishOrderAsync(context, cancellationToken),
                new RunOptions
                {
                    Iterations = 1,
                    Seed = 2,
                    Schedule = ReplaySchedule.Parse(replayText),
                },
                cancellationToken));

        _ = await Assert.That(replayed.FailureOrigin).IsEqualTo(RunFailureOrigin.UserTest);
        _ = await Assert.That(replayed.InnerException).IsNotTypeOf<RunException>();
        _ = await Assert.That(replayed.SchedulerDiagnostics!.UnusedReplaySteps).IsEmpty();
    }

    /// <summary>Verifies exploration runs only the schedules within MaxSchedules: it passes when the failing one lies beyond, and finds it with a larger bound.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ExploreStopsAtMaxSchedules(CancellationToken cancellationToken)
    {
        // The first schedule releases "first" before "second", which passes; the second schedule fails.
        await Runner.ExploreAsync(
            static options => options.WithMaxSchedules(1).WithMaxStepsPerSchedule(10),
            braid => RunOrderDependentExploreAsync(braid, cancellationToken),
            cancellationToken);

        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.ExploreAsync(
                static options => options.WithMaxSchedules(2).WithMaxStepsPerSchedule(10),
                braid => RunOrderDependentExploreAsync(braid, cancellationToken),
                cancellationToken));

        _ = await Assert.That(exception.FailureOrigin).IsEqualTo(RunFailureOrigin.UserTest);
        _ = await Assert.That(exception.InnerException).IsTypeOf<InvalidOperationException>();
        _ = await Assert.That(exception.InnerException!.Message).IsEqualTo("second ran before first");
    }

    /// <summary>Verifies exhausting MaxSchedules returns without failure when only a passing schedule is evaluated.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ExploreCompletesWhenOnlyPassingSchedule(CancellationToken cancellationToken)
    {
        _ = await Assert.That(() => Runner.ExploreAsync(
            static options => options.WithMaxSchedules(1).WithMaxStepsPerSchedule(2),
            async braid =>
            {
                await braid.WorkerAsync("worker-1", async () => await Probe.HitAsync("only-probe", cancellationToken));
                await braid.JoinAsync(cancellationToken);
            },
            cancellationToken)).ThrowsNothing();
    }

    /// <summary>Verifies a user InvalidOperationException thrown by the callback of the first run is not suppressed.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ExploreSurfacesUserInvalidOp(CancellationToken cancellationToken)
    {
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.ExploreAsync(
                static options => options.WithMaxSchedules(10).WithMaxStepsPerSchedule(4),
                static _ => throw new InvalidOperationException("user test failure"),
                cancellationToken));

        _ = await Assert.That(exception.FailureOrigin).IsEqualTo(RunFailureOrigin.UserTest);
        _ = await Assert.That(exception.InnerException).IsTypeOf<InvalidOperationException>();
    }

    private static async Task AssertStableReaderWriterIdsAsync(RunException exception)
    {
        _ = await Assert.That(exception.Traces).Contains("reader forked");
        _ = await Assert.That(exception.Traces).Contains("writer forked");
        _ = await Assert.That(exception.Traces).Contains("reader hit ready");
        _ = await Assert.That(exception.Traces).Contains("writer hit ready");
        var hasWorkerPrefix = false;
        foreach (var line in exception.Traces)
        {
            if (!line.StartsWith("worker-", StringComparison.Ordinal))
                continue;
            hasWorkerPrefix = true;
            break;
        }

        _ = await Assert.That(hasWorkerPrefix).IsFalse().Because("Trace should not contain worker-prefixed entries.");

        _ = await Assert.That(exception.TryGetReplayText(out var replayText, out var error)).IsTrue().Because(error!);
        _ = await Assert.That(replayText).Contains("reader");
        _ = await Assert.That(replayText).Contains("writer");

        _ = await Assert.That(exception.Steps).Contains(static step => string.Equals(step.WorkerId, "reader", StringComparison.Ordinal) && string.Equals(step.ProbeName, "ready", StringComparison.Ordinal));
        _ = await Assert.That(exception.Steps).Contains(static step => string.Equals(step.WorkerId, "writer", StringComparison.Ordinal) && string.Equals(step.ProbeName, "ready", StringComparison.Ordinal));
    }

    private static Task<RunException> ExploreLostUpdateAsync(CancellationToken cancellationToken = default) => BraidAssertions.AssertExpectsAsync<RunException>(
        Runner.ExploreAsync(static options => options.WithMaxSchedules(40).WithMaxStepsPerSchedule(10), braid => RunLostUpdateExploreAsync(braid, cancellationToken), cancellationToken));

    private static Task<RunException> ExploreReaderWriterStableIdsAsync(CancellationToken cancellationToken = default) => BraidAssertions.AssertExpectsAsync<RunException>(
        Runner.ExploreAsync(
            static options => options.WithMaxSchedules(10).WithMaxStepsPerSchedule(4),
            braid => RegisterReaderWriterWorkersAsync(braid, cancellationToken),
            cancellationToken));

    private static async Task RegisterReaderWriterWorkersAsync(ExploreContext braid, CancellationToken cancellationToken = default)
    {
        var completedWorkers = 0;

        await braid.WorkerAsync(
            "reader",
            async () =>
            {
                await Probe.HitAsync("ready", cancellationToken);
                _ = Interlocked.Increment(ref completedWorkers);
            });

        await braid.WorkerAsync(
            "writer",
            async () =>
            {
                await Probe.HitAsync("ready", cancellationToken);
                _ = Interlocked.Increment(ref completedWorkers);
            });

        await braid.JoinAsync(cancellationToken);
        _ = await Assert.That(completedWorkers).IsEqualTo(0);
    }

    private static async Task RunFinishOrderExploreAsync(ExploreContext braid, CancellationToken cancellationToken = default)
    {
        string? finishedFirst = null;

        await braid.WorkerAsync(
            "first",
            async () =>
            {
                await Probe.HitAsync("a1", cancellationToken);
                await Probe.HitAsync("a2", cancellationToken);
                finishedFirst ??= "first";
            });

        await braid.WorkerAsync(
            "second",
            async () =>
            {
                await Probe.HitAsync("b1", cancellationToken);
                await Probe.HitAsync("b2", cancellationToken);
                finishedFirst ??= "second";
            });

        await braid.JoinAsync(cancellationToken);
        _ = await Assert.That(finishedFirst).IsEqualTo("first");
    }

    private static async Task RunFinishOrderAsync(RunContext context, CancellationToken cancellationToken = default)
    {
        string? finishedFirst = null;

        context.Fork(
            "first",
            async () =>
            {
                await Probe.HitAsync("a1", cancellationToken);
                await Probe.HitAsync("a2", cancellationToken);
                finishedFirst ??= "first";
            });

        context.Fork(
            "second",
            async () =>
            {
                await Probe.HitAsync("b1", cancellationToken);
                await Probe.HitAsync("b2", cancellationToken);
                finishedFirst ??= "second";
            });

        await context.JoinAsync(cancellationToken);
        _ = await Assert.That(finishedFirst).IsEqualTo("first");
    }

    private static async Task RunOrderDependentExploreAsync(ExploreContext braid, CancellationToken cancellationToken = default)
    {
        var firstDone = 0;

        await braid.WorkerAsync(
            "first",
            async () =>
            {
                await Probe.HitAsync("a", cancellationToken);
                firstDone = 1;
            });

        await braid.WorkerAsync(
            "second",
            async () =>
            {
                await Probe.HitAsync("b", cancellationToken);
                if (firstDone == 0)
                    throw new InvalidOperationException("second ran before first");
            });

        await braid.JoinAsync(cancellationToken);
    }

    private static async Task RunLostUpdateExploreAsync(ExploreContext braid, CancellationToken cancellationToken = default)
    {
        var value = 0;

        await braid.WorkerAsync(
            "worker-1",
            async () =>
            {
                var current = value;
                await Probe.HitAsync("after-read", cancellationToken);
                await Probe.HitAsync("before-write", cancellationToken);
                value = current + 1;
            });

        await braid.WorkerAsync(
            "worker-2",
            async () =>
            {
                var current = value;
                await Probe.HitAsync("after-read", cancellationToken);
                await Probe.HitAsync("before-write", cancellationToken);
                value = current + 1;
            });

        await braid.JoinAsync(cancellationToken);
        _ = await Assert.That(value).IsEqualTo(2);
    }

    private static async Task RunNamedLostUpdateExploreAsync(ExploreContext braid, CancellationToken cancellationToken = default)
    {
        var value = 0;

        await braid.WorkerAsync(
            "reader",
            async () =>
            {
                var current = value;
                await Probe.HitAsync("after-read", cancellationToken);
                await Probe.HitAsync("before-write", cancellationToken);
                value = current + 1;
            });

        await braid.WorkerAsync(
            "writer",
            async () =>
            {
                var current = value;
                await Probe.HitAsync("after-read", cancellationToken);
                await Probe.HitAsync("before-write", cancellationToken);
                value = current + 1;
            });

        await braid.JoinAsync(cancellationToken);
        _ = await Assert.That(value).IsEqualTo(2);
    }
}
