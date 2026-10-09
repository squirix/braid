namespace Braid.Tests;

/// <summary>Covers that a replay schedule decides a lost update when every shared access follows a probe.</summary>
public sealed class LostUpdateScheduleTests : TestBase
{
    /// <summary>Verifies the interleaved schedule loses an update.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task InterleavedScheduleLosesUpdate(CancellationToken cancellationToken)
    {
        var schedule = ReplaySchedule.Replay(
            ReplayStep.Hit("worker-1", "before-read"),
            ReplayStep.Hit("worker-2", "before-read"),
            ReplayStep.Hit("worker-1", "before-write"),
            ReplayStep.Hit("worker-2", "before-write"));

        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(RunIncrementsAsync(schedule, cancellationToken));

        _ = await Assert.That(exception.FailureOrigin).IsEqualTo(RunFailureOrigin.UserTest);
    }

    /// <summary>Verifies the sequential schedule keeps both updates.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task SequentialScheduleKeepsBothUpdates(CancellationToken cancellationToken)
    {
        var schedule = ReplaySchedule.Replay(
            ReplayStep.Hit("worker-1", "before-read"),
            ReplayStep.Hit("worker-1", "before-write"),
            ReplayStep.Hit("worker-2", "before-read"),
            ReplayStep.Hit("worker-2", "before-write"));

        _ = await Assert.That(() => RunIncrementsAsync(schedule, cancellationToken)).ThrowsNothing();
    }

    private static Task RunIncrementsAsync(ReplaySchedule schedule, CancellationToken cancellationToken)
    {
        return Runner.RunAsync(
            async context =>
            {
                var value = 0;

                context.Fork(
                    "worker-1",
                    async () =>
                    {
                        await Probe.HitAsync("before-read", cancellationToken);
                        var current = value;
                        await Probe.HitAsync("before-write", cancellationToken);
                        value = current + 1;
                    });

                context.Fork(
                    "worker-2",
                    async () =>
                    {
                        await Probe.HitAsync("before-read", cancellationToken);
                        var current = value;
                        await Probe.HitAsync("before-write", cancellationToken);
                        value = current + 1;
                    });

                await context.JoinAsync(cancellationToken);
                if (value != 2)
                    throw new InvalidOperationException($"Lost update: value is {value}.");
            },
            new RunOptions { Iterations = 1, Seed = 1, Schedule = schedule },
            cancellationToken);
    }
}
