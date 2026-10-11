#:sdk Microsoft.NET.Sdk
#:property PublishAot=false
#:project ../../../src/braid/Braid.csproj
#:package xunit.v3@3.2.2
#:package Microsoft.NET.Test.Sdk@18.7.0

using Xunit;

namespace Braid.Examples.LostUpdate;

/// <summary>Demonstrates turning a lost-update interleaving into a stable replay regression.</summary>
public sealed class LostUpdateTests
{
    private static CancellationToken TestCancellationToken => TestContext.Current.CancellationToken;

    /// <summary>Verifies a scripted schedule reproduces the lost update and exports a replay token.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ReplayTokenCapturesLostUpdateInterleaving()
    {
        var schedule = ReplaySchedule.Parse("hit worker-1 before-read\nhit worker-2 before-read\nhit worker-1 before-write\nhit worker-2 before-write\n");

        var exception = await Assert.ThrowsAsync<RunException>(() => RunIncrementsAsync(schedule));

        Assert.True(exception.TryGetReplayText(out var replayText, out var error), error);
        Assert.Equal(schedule.ToReplayText(), replayText);
    }

    /// <summary>Verifies the sequential schedule keeps both increments, so the token above really selects the race.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public Task SequentialScheduleKeepsBothIncrements()
    {
        var schedule = ReplaySchedule.Parse("hit worker-1 before-read\nhit worker-1 before-write\nhit worker-2 before-read\nhit worker-2 before-write\n");
        return RunIncrementsAsync(schedule);
    }

    private static Task RunIncrementsAsync(ReplaySchedule schedule)
    {
        var options = new RunOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = schedule,
        };

        return Runner.RunAsync(
            static async context =>
            {
                var value = 0;

                context.Fork(async () =>
                {
                    await Probe.HitAsync("before-read", TestCancellationToken);
                    var current = value;
                    await Probe.HitAsync("before-write", TestCancellationToken);
                    value = current + 1;
                });

                context.Fork(async () =>
                {
                    await Probe.HitAsync("before-read", TestCancellationToken);
                    var current = value;
                    await Probe.HitAsync("before-write", TestCancellationToken);
                    value = current + 1;
                });

                await context.JoinAsync(TestCancellationToken);

                Assert.Equal(2, value);
            },
            options,
            TestCancellationToken);
    }
}
