using Xunit;

namespace Braid.Tests;

/// <summary>Covers deterministic seed behavior.</summary>
public sealed class BraidDeterministicSeedTests : TestBase
{
    /// <summary>Verifies different seeds can explore different random traces.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncDifferentSeedsDifferentTraces()
    {
        var traces = new HashSet<string>(StringComparer.Ordinal);

        for (var seed = 100; seed < 116; seed++)
        {
            var trace = await CaptureRandomTraceAsync(seed);
            _ = traces.Add(string.Join('|', trace));
        }

        Assert.True(traces.Count >= 2, "Expected several seeds to produce at least two distinct random traces.");
    }

    /// <summary>Verifies scripted replay does not depend on the random seed.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncScriptedIgnoresRandomSeed()
    {
        var schedule = ReplaySchedule.Replay(new ReplayStep("worker-3", "ready"), new ReplayStep("worker-1", "ready"), new ReplayStep("worker-2", "ready"));

        var (trace, releaseOrder) = await CaptureScriptedRunAsync(12345, schedule);
        var (actual, order) = await CaptureScriptedRunAsync(67890, schedule);

        Assert.Equal(["worker-3", "worker-1", "worker-2"], releaseOrder);
        Assert.Equal(releaseOrder, order);
        Assert.Equal(trace, actual);
    }

    /// <summary>Verifies random scheduling produces the same trace for the same seed.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncWithSameSeedProducesSameTrace()
    {
        var first = await CaptureRandomTraceAsync(12345);
        var second = await CaptureRandomTraceAsync(12345);

        Assert.Equal(first, second);
    }

    private static async Task<IReadOnlyList<string>> CaptureRandomTraceAsync(int seed)
    {
        var exception = await Assertions.ExpectsAsync<RunException>(
            Runner.RunAsync(
                static async context =>
                {
                    for (var index = 0; index < 5; index++)
                        context.Fork(static async () => await Probe.HitAsync("ready", DefaultCancellationToken));

                    await context.JoinAsync(DefaultCancellationToken);
                    throw new InvalidOperationException("capture trace");
                },
                new RunOptions { Iterations = 1, Seed = seed },
                DefaultCancellationToken));

        return exception.Traces;
    }

    private static async Task<(IReadOnlyList<string> Trace, IReadOnlyList<string> ReleaseOrder)> CaptureScriptedRunAsync(int seed, ReplaySchedule schedule)
    {
        var releases = new List<string>();
        var exception = await Assertions.ExpectsAsync<RunException>(
            Runner.RunAsync(
                async context =>
                {
                    for (var index = 0; index < 3; index++)
                        ForkHitReadyAddWorker(context, releases, $"worker-{index + 1}");

                    await context.JoinAsync(DefaultCancellationToken);
                    throw new InvalidOperationException("capture trace");
                },
                new RunOptions { Iterations = 1, Seed = seed, Schedule = schedule },
                DefaultCancellationToken));

        return (exception.Traces, releases);
    }
}
