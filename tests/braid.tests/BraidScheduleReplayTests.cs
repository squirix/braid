using TUnit.Assertions.Enums;

namespace Braid.Tests;

/// <summary>Covers scripted schedule replay behavior.</summary>
public sealed class BraidScheduleReplayTests : TestBase
{
    /// <summary>Verifies scripted schedules release workers in the requested order.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task RunAsyncReleasesWorkersInScriptedOrder(CancellationToken cancellationToken)
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
                    await Probe.HitAsync("ready", cancellationToken);
                    releases.Add("worker-1");
                });

                context.Fork(async () =>
                {
                    await Probe.HitAsync("ready", cancellationToken);
                    releases.Add("worker-2");
                });

                await context.JoinAsync(cancellationToken);
            },
            options,
            cancellationToken);

        _ = await Assert.That(releases).IsEquivalentTo(["worker-2", "worker-1"], CollectionOrdering.Matching);
    }

    /// <summary>Verifies scripted schedules can reproduce a lost update.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task RunAsyncReplaysReproducesLostUpdate(CancellationToken cancellationToken)
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

        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                async context =>
                {
                    var value = 0;

                    context.Fork(async () =>
                    {
                        var current = value;
                        await Probe.HitAsync("after-read", cancellationToken);
                        await Probe.HitAsync("before-write", cancellationToken);
                        value = current + 1;
                    });

                    context.Fork(async () =>
                    {
                        var current = value;
                        await Probe.HitAsync("after-read", cancellationToken);
                        await Probe.HitAsync("before-write", cancellationToken);
                        value = current + 1;
                    });

                    await context.JoinAsync(cancellationToken);

                    _ = await Assert.That(value).IsEqualTo(2);
                },
                options,
                cancellationToken));

        _ = await Assert.That(exception.Seed).IsEqualTo(12345);
        foreach (var marker in new[] { "worker-1", "worker-2", "after-read", "before-write" })
        {
            var found = false;
            foreach (var line in exception.Traces)
            {
                if (!line.Contains(marker, StringComparison.Ordinal))
                    continue;
                found = true;
                break;
            }

            _ = await Assert.That(found).IsTrue().Because($"Trace should mention '{marker}'.");
        }
    }
}
