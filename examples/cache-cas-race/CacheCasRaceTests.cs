using Xunit;

namespace Braid.Examples.CacheCasRace;

/// <summary>
/// Demonstrates a deterministic compare-and-set race on a versioned cell.
/// </summary>
public sealed class CacheCasRaceTests
{
    private static CancellationToken TestCancellationToken => TestContext.Current.CancellationToken;

    /// <summary>
    /// Verifies compare-and-set returns <see cref="CasResult.VersionMismatch"/> when another worker updates the cell between read and CAS.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task CompareAndSetReturnsVersionMismatchWhenEntryChangesBetweenReadAndCas()
    {
        var cell = new VersionedCell<string>("initial");
        CasResult? worker1Result = null;

        var options = new BraidOptions
        {
            Iterations = 1,
            Schedule = BraidSchedule.Replay(BraidStep.Arrive("worker-1", "before-cas"), BraidStep.Hit("worker-2", "updated"), BraidStep.Release("worker-1", "before-cas")),
        };

        await Braid.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    var entry = await cell.GetAsync(TestCancellationToken);
                    Assert.Equal("initial", entry.Value);
                    Assert.Equal(1L, entry.Version);
                    await BraidProbe.HitAsync("before-cas", TestCancellationToken);
                    worker1Result = await cell.CompareAndSetAsync(entry.Version, "worker-1", TestCancellationToken);
                });

                context.Fork(async () =>
                {
                    await cell.SetAsync("worker-2", TestCancellationToken);
                    await BraidProbe.HitAsync("updated", TestCancellationToken);
                });

                await context.JoinAsync(TestCancellationToken);
            },
            options,
            TestCancellationToken);

        Assert.Equal(CasResult.VersionMismatch, worker1Result);
    }

    /// <summary>
    /// Verifies the same interleaving when the schedule is loaded from replay text.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task CompareAndSetReturnsVersionMismatchWhenScheduleIsParsedFromReplayText()
    {
        var cell = new VersionedCell<string>("initial");
        CasResult? worker1Result = null;

        var schedule = BraidSchedule.Parse(
            """
            arrive worker-1 before-cas
            hit worker-2 updated
            release worker-1 before-cas
            """);

        var options = new BraidOptions { Iterations = 1, Schedule = schedule };

        await Braid.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    var entry = await cell.GetAsync(TestCancellationToken);
                    Assert.Equal("initial", entry.Value);
                    Assert.Equal(1L, entry.Version);
                    await BraidProbe.HitAsync("before-cas", TestCancellationToken);
                    worker1Result = await cell.CompareAndSetAsync(entry.Version, "worker-1", TestCancellationToken);
                });

                context.Fork(async () =>
                {
                    await cell.SetAsync("worker-2", TestCancellationToken);
                    await BraidProbe.HitAsync("updated", TestCancellationToken);
                });

                await context.JoinAsync(TestCancellationToken);
            },
            options,
            TestCancellationToken);

        Assert.Equal(CasResult.VersionMismatch, worker1Result);
    }
}
