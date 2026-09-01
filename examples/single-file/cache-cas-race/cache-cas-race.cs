#:sdk Microsoft.NET.Sdk
#:property PublishAot=false
#:project ../../../src/braid/Braid.csproj
#:package xunit.v3@3.2.2
#:package Microsoft.NET.Test.Sdk@18.7.0
#:include CasResult.cs
#:include VersionedEntry.cs
#:include VersionedCell.cs

using Xunit;

namespace Braid.Examples.CacheCasRace;

/// <summary>Demonstrates a deterministic compare-and-set race on a versioned cell.</summary>
public sealed class CacheCasRaceTests
{
    private static CancellationToken TestCancellationToken => TestContext.Current.CancellationToken;

    /// <summary>
    /// Verifies compare-and-set returns <see cref="CasResult.VersionMismatch" /> when another worker updates the cell between read and CAS.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task CompareAndSetReturnsVersionMismatchWhenEntryChangesBetweenReadAndCas()
    {
        var cell = new VersionedCell<string>("initial");
        CasResult? worker1Result = null;

        var options = new RunOptions
        {
            Iterations = 1,
            Schedule = ReplaySchedule.Replay(ReplayStep.Arrive("worker-1", "before-cas"), ReplayStep.Hit("worker-2", "updated"), ReplayStep.Release("worker-1", "before-cas")),
        };

        await Runner.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    var entry = await cell.GetAsync(TestCancellationToken);
                    Assert.Equal("initial", entry.Value);
                    Assert.Equal(1L, entry.Version);
                    await Probe.HitAsync("before-cas", TestCancellationToken);
                    worker1Result = await cell.CompareAndSetAsync(entry.Version, "worker-1", TestCancellationToken);
                });

                context.Fork(async () =>
                {
                    await cell.SetAsync("worker-2", TestCancellationToken);
                    await Probe.HitAsync("updated", TestCancellationToken);
                });

                await context.JoinAsync(TestCancellationToken);
            },
            options,
            TestCancellationToken);

        Assert.Equal(CasResult.VersionMismatch, worker1Result);
    }

    /// <summary>Verifies the same interleaving when the schedule is loaded from replay text.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task CompareAndSetReturnsVersionMismatchWhenScheduleIsParsedFromReplayText()
    {
        var cell = new VersionedCell<string>("initial");
        CasResult? worker1Result = null;

        var schedule = ReplaySchedule.Parse("arrive worker-1 before-cas\nhit worker-2 updated\nrelease worker-1 before-cas\n");

        var options = new RunOptions { Iterations = 1, Schedule = schedule };

        await Runner.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    var entry = await cell.GetAsync(TestCancellationToken);
                    Assert.Equal("initial", entry.Value);
                    Assert.Equal(1L, entry.Version);
                    await Probe.HitAsync("before-cas", TestCancellationToken);
                    worker1Result = await cell.CompareAndSetAsync(entry.Version, "worker-1", TestCancellationToken);
                });

                context.Fork(async () =>
                {
                    await cell.SetAsync("worker-2", TestCancellationToken);
                    await Probe.HitAsync("updated", TestCancellationToken);
                });

                await context.JoinAsync(TestCancellationToken);
            },
            options,
            TestCancellationToken);

        Assert.Equal(CasResult.VersionMismatch, worker1Result);
    }
}
