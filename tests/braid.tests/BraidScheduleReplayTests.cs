using Xunit;

namespace Braid.Tests;

/// <summary>Covers scripted schedule replay behavior.</summary>
public sealed class BraidScheduleReplayTests : TestBase
{
    /// <summary>Verifies scripted schedules release workers in the requested order.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncReleasesWorkersInScriptedOrder()
    {
        var releases = new List<string>();
        var options = new RunOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = ReplaySchedule.Replay(new ReplayStep("worker-2", "ready"), new ReplayStep("worker-1", "ready")),
        };

        await Runner.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    await Probe.HitAsync("ready", DefaultCancellationToken);
                    releases.Add("worker-1");
                });

                context.Fork(async () =>
                {
                    await Probe.HitAsync("ready", DefaultCancellationToken);
                    releases.Add("worker-2");
                });

                await context.JoinAsync(DefaultCancellationToken);
            },
            options,
            DefaultCancellationToken);

        Assert.Equal(["worker-2", "worker-1"], releases);
    }

    /// <summary>Verifies scripted schedules can reproduce a lost update.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncReplaysReproducesLostUpdate()
    {
        var options = new RunOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = ReplaySchedule.Replay(
                new ReplayStep("worker-1", "after-read"),
                new ReplayStep("worker-2", "after-read"),
                new ReplayStep("worker-1", "before-write"),
                new ReplayStep("worker-2", "before-write")),
        };

        var exception = await Assertions.ExpectsAsync<RunException>(
            Runner.RunAsync(
                static async context =>
                {
                    var value = 0;

                    context.Fork(async () =>
                    {
                        var current = value;
                        await Probe.HitAsync("after-read", DefaultCancellationToken);
                        await Probe.HitAsync("before-write", DefaultCancellationToken);
                        value = current + 1;
                    });

                    context.Fork(async () =>
                    {
                        var current = value;
                        await Probe.HitAsync("after-read", DefaultCancellationToken);
                        await Probe.HitAsync("before-write", DefaultCancellationToken);
                        value = current + 1;
                    });

                    await context.JoinAsync(DefaultCancellationToken);

                    Assert.Equal(2, value);
                },
                options,
                DefaultCancellationToken));

        Assert.Equal(12345, exception.Seed);
        Assert.Contains(exception.Trace, static line => line.Contains("worker-1", StringComparison.Ordinal));
        Assert.Contains(exception.Trace, static line => line.Contains("worker-2", StringComparison.Ordinal));
        Assert.Contains(exception.Trace, static line => line.Contains("after-read", StringComparison.Ordinal));
        Assert.Contains(exception.Trace, static line => line.Contains("before-write", StringComparison.Ordinal));
    }
}
