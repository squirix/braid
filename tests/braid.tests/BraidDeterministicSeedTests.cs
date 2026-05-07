using Xunit;

namespace Braid.Tests;

/// <summary>
/// Covers deterministic seed behavior.
/// </summary>
public sealed class BraidDeterministicSeedTests : TestBase
{
    /// <summary>
    /// Verifies random scheduling produces the same trace for the same seed.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncWithSameSeedProducesSameTrace()
    {
        var first = await CaptureRandomTraceAsync(seed: 12345);
        var second = await CaptureRandomTraceAsync(seed: 12345);

        Assert.Equal(first, second);
    }

    /// <summary>
    /// Verifies different seeds can explore different random traces.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncWithDifferentSeedsCanProduceDifferentTraces()
    {
        var traces = new HashSet<string>(StringComparer.Ordinal);

        for (var seed = 100; seed < 116; seed++)
        {
            var trace = await CaptureRandomTraceAsync(seed);
            _ = traces.Add(string.Join("|", trace));
        }

        Assert.True(traces.Count >= 2, "Expected several seeds to produce at least two distinct random traces.");
    }

    /// <summary>
    /// Verifies scripted replay does not depend on the random seed.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncWithScriptedScheduleIgnoresRandomSeed()
    {
        var schedule = BraidSchedule.Replay(
            new BraidStep("worker-3", "ready"),
            new BraidStep("worker-1", "ready"),
            new BraidStep("worker-2", "ready"));

        var (firstTrace, firstReleaseOrder) = await CaptureScriptedRunAsync(seed: 12345, schedule);
        var (secondTrace, secondReleaseOrder) = await CaptureScriptedRunAsync(seed: 67890, schedule);

        Assert.Equal(["worker-3", "worker-1", "worker-2"], firstReleaseOrder);
        Assert.Equal(firstReleaseOrder, secondReleaseOrder);
        Assert.Equal(firstTrace, secondTrace);
    }

    private static async Task<IReadOnlyList<string>> CaptureRandomTraceAsync(int seed)
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(async () =>
        {
            await Braid.RunAsync(
                static async context =>
                {
                    for (var index = 0; index < 5; index++)
                    {
                        context.Fork(static async () => { await BraidProbe.HitAsync("ready", DefaultCancellationToken); });
                    }

                    await context.JoinAsync(DefaultCancellationToken);
                    throw new InvalidOperationException("capture trace");
                },
                new BraidOptions { Iterations = 1, Seed = seed },
                DefaultCancellationToken);
        });

        return exception.Trace;
    }

    private static async Task<(IReadOnlyList<string> Trace, IReadOnlyList<string> ReleaseOrder)> CaptureScriptedRunAsync(int seed, BraidSchedule schedule)
    {
        var releases = new List<string>();
        var exception = await Assert.ThrowsAsync<BraidRunException>(async () =>
        {
            await Braid.RunAsync(
                async context =>
                {
                    for (var index = 0; index < 3; index++)
                    {
                        var workerName = $"worker-{index + 1}";
                        context.Fork(async () =>
                        {
                            await BraidProbe.HitAsync("ready", DefaultCancellationToken);
                            releases.Add(workerName);
                        });
                    }

                    await context.JoinAsync(DefaultCancellationToken);
                    throw new InvalidOperationException("capture trace");
                },
                new BraidOptions { Iterations = 1, Seed = seed, Schedule = schedule },
                DefaultCancellationToken);
        });

        return (exception.Trace, releases);
    }
}
