#:sdk Microsoft.NET.Sdk
#:property PublishAot=false
#:project ../../../src/braid/Braid.csproj
#:package xunit.v3@3.2.2
#:package Microsoft.NET.Test.Sdk@18.7.0

using Xunit;

namespace Braid.Examples.ExploreLostUpdate;

/// <summary>Demonstrates bounded exploration that discovers a lost-update interleaving and exports a replay token.</summary>
public sealed class ExploreLostUpdateTests
{
    private static CancellationToken TestCancellationToken => TestContext.Current.CancellationToken;

    /// <summary>Verifies exploration finds the race, exports a replay token, and reproduces the failure under a replay schedule.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ExploreAsyncDiscoversLostUpdateReplayToken()
    {
        var exception = await Assert.ThrowsAsync<RunException>(static async () =>
        {
            await Runner.ExploreAsync(
                static options => options
                    .WithSeed(12_345)
                    .WithMaxSchedules(100)
                    .WithMaxStepsPerSchedule(10),
                RunLostUpdateExploreAsync,
                TestCancellationToken);
        });

        Assert.True(exception.TryGetReplayText(out var replayText, out var error), error);
        Assert.Contains("after-read", replayText, StringComparison.Ordinal);
        Assert.Contains("before-write", replayText, StringComparison.Ordinal);
        Assert.Contains("reader", replayText, StringComparison.Ordinal);
        Assert.Contains("writer", replayText, StringComparison.Ordinal);

        var schedule = ReplaySchedule.Parse(replayText);

        _ = await Assert.ThrowsAsync<RunException>(async () =>
        {
            await Runner.RunAsync(
                RunLostUpdateRunAsync,
                new RunOptions
                {
                    Iterations = 1,
                    Seed = 12_345,
                    Schedule = schedule,
                },
                TestCancellationToken);
        });
    }

    private static async Task RunLostUpdateExploreAsync(ExploreContext braid)
    {
        var value = 0;

        await braid.WorkerAsync(
            "reader",
            async () =>
            {
                var current = value;
                await Probe.HitAsync("after-read", TestCancellationToken);
                await Probe.HitAsync("before-write", TestCancellationToken);
                value = current + 1;
            });

        await braid.WorkerAsync(
            "writer",
            async () =>
            {
                var current = value;
                await Probe.HitAsync("after-read", TestCancellationToken);
                await Probe.HitAsync("before-write", TestCancellationToken);
                value = current + 1;
            });

        await braid.JoinAsync(TestCancellationToken);
        Assert.Equal(2, value);
    }

    private static async Task RunLostUpdateRunAsync(RunContext context)
    {
        var value = 0;

        context.Fork(
            "reader",
            async () =>
            {
                var current = value;
                await Probe.HitAsync("after-read", TestCancellationToken);
                await Probe.HitAsync("before-write", TestCancellationToken);
                value = current + 1;
            });

        context.Fork(
            "writer",
            async () =>
            {
                var current = value;
                await Probe.HitAsync("after-read", TestCancellationToken);
                await Probe.HitAsync("before-write", TestCancellationToken);
                value = current + 1;
            });

        await context.JoinAsync(TestCancellationToken);
        Assert.Equal(2, value);
    }
}
