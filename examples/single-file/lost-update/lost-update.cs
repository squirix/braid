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
        var schedule = BraidSchedule.Parse("hit worker-1 after-read\nhit worker-2 after-read\nhit worker-1 before-write\nhit worker-2 before-write\n");

        var options = new BraidOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = schedule,
        };

        var exception = await Assert.ThrowsAsync<BraidRunException>(async () =>
        {
            await BraidRunner.RunAsync(
                static async context =>
                {
                    var value = 0;

                    context.Fork(async () =>
                    {
                        var current = value;
                        await BraidProbe.HitAsync("after-read", TestCancellationToken);
                        await BraidProbe.HitAsync("before-write", TestCancellationToken);
                        value = current + 1;
                    });

                    context.Fork(async () =>
                    {
                        var current = value;
                        await BraidProbe.HitAsync("after-read", TestCancellationToken);
                        await BraidProbe.HitAsync("before-write", TestCancellationToken);
                        value = current + 1;
                    });

                    await context.JoinAsync(TestCancellationToken);

                    Assert.Equal(2, value);
                },
                options,
                TestCancellationToken);
        });

        Assert.True(exception.TryGetReplayText(out var replayText, out var error), error);
        Assert.Equal(schedule.ToReplayText(), replayText);
    }
}
