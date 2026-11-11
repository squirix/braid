using System;
using System.Threading;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace Braid.Tests;

/// <summary>Covers replay export from recorded scheduling decisions.</summary>
public sealed class ExecutedReplayTests : TestBase
{
    /// <summary>Verifies a random failure exports its start and hit decisions and can be replayed.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task RandomFailureReplaysRecordedSteps(CancellationToken cancellationToken)
    {
        var failure = await RunFailingWorkerAsync(null, cancellationToken);

        _ = await Assert.That(failure.Steps).IsEmpty();
        _ = await Assert.That(failure.TryGetExecutedReplayText(out var text, out var error)).IsTrue().Because(error!);
        _ = await Assert.That(failure.ToString()).Contains(text);
        var schedule = ReplaySchedule.Parse(text);
        _ = await Assert.That(schedule.Steps).IsEquivalentTo(
            [ReplayStep.Start("worker"), ReplayStep.Hit("worker", "ready")], CollectionOrdering.Matching);

        var replayed = await RunFailingWorkerAsync(schedule, cancellationToken);
        _ = await Assert.That(replayed.FailureOrigin).IsEqualTo(RunFailureOrigin.UserTest);
        _ = await Assert.That(replayed.InnerException!.Message).IsEqualTo("worker failed");
    }

    /// <summary>Verifies the executed prefix includes implicit starts and holds but excludes unused steps.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task RecordedReplayOmitsUnusedSteps(CancellationToken cancellationToken)
    {
        var schedule = ReplaySchedule.Replay(
            ReplayStep.Arrive("worker", "ready"), ReplayStep.Release("worker", "ready"), ReplayStep.Hit("worker", "unused"));
        var failure = await RunFailingWorkerAsync(schedule, cancellationToken);

        _ = await Assert.That(failure.Context.ExecutedSteps).IsEquivalentTo(
            [ReplayStep.Start("worker"), ReplayStep.Arrive("worker", "ready"), ReplayStep.Release("worker", "ready")],
            CollectionOrdering.Matching);
        _ = await Assert.That(failure.SchedulerDiagnostics!.UnusedReplaySteps).HasSingleItem();
        _ = await Assert.That(failure.TryGetExecutedReplayText(out var text, out var error)).IsTrue().Because(error!);

        var replayed = await RunFailingWorkerAsync(ReplaySchedule.Parse(text), cancellationToken);
        _ = await Assert.That(replayed.FailureOrigin).IsEqualTo(RunFailureOrigin.UserTest);
        _ = await Assert.That(replayed.InnerException!.Message).IsEqualTo("worker failed");
    }

    /// <summary>Verifies a failure before scheduling has no executed replay text.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task NoExecutedReplayBeforeScheduling(CancellationToken cancellationToken)
    {
        var failure = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                static _ => Task.FromException(new InvalidOperationException("setup failed")),
                new RunOptions { Iterations = 1 },
                cancellationToken));

        _ = await Assert.That(failure.Context.ExecutedSteps).IsEmpty();
        _ = await Assert.That(failure.TryGetExecutedReplayText(out _, out _)).IsFalse();
    }

    private static Task<RunException> RunFailingWorkerAsync(ReplaySchedule? schedule, CancellationToken cancellationToken) =>
        BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                context =>
                {
                    context.Fork("worker", async () =>
                    {
                        await Probe.HitAsync("ready", cancellationToken);
                        throw new InvalidOperationException("worker failed");
                    });
                    return context.JoinAsync(cancellationToken);
                },
                new RunOptions { Iterations = 1, Seed = 123, Schedule = schedule },
                cancellationToken));
}
