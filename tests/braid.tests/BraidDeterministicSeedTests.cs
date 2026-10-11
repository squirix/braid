using TUnit.Assertions.Enums;

namespace Braid.Tests;

/// <summary>Covers deterministic seed behavior.</summary>
public sealed class BraidDeterministicSeedTests : TestBase
{
    /// <summary>Verifies different seeds can explore different random traces.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task RunAsyncDifferentSeedsDifferentTraces(CancellationToken cancellationToken)
    {
        var traces = new HashSet<string>(StringComparer.Ordinal);

        for (var seed = 100; seed < 116; seed++)
        {
            var trace = await CaptureRandomTraceAsync(seed, cancellationToken);
            _ = traces.Add(string.Join('|', trace));
        }

        _ = await Assert.That(traces.Count >= 2).IsTrue().Because("Expected several seeds to produce at least two distinct random traces.");
    }

    /// <summary>Verifies scripted replay does not depend on the random seed.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task RunAsyncScriptedIgnoresRandomSeed(CancellationToken cancellationToken)
    {
        var schedule = ReplaySchedule.Replay(new ReplayStep("worker-3", "ready"), new ReplayStep("worker-1", "ready"), new ReplayStep("worker-2", "ready"));

        var (trace, releaseOrder) = await CaptureScriptedRunAsync(12345, schedule, cancellationToken);
        var (actual, order) = await CaptureScriptedRunAsync(67890, schedule, cancellationToken);

        _ = await Assert.That(releaseOrder).IsEquivalentTo(["worker-3", "worker-1", "worker-2"], CollectionOrdering.Matching);
        _ = await Assert.That(order).IsEquivalentTo(releaseOrder, CollectionOrdering.Matching);
        _ = await Assert.That(actual).IsEquivalentTo(trace, CollectionOrdering.Matching);
    }

    /// <summary>Verifies random scheduling produces the same trace for the same seed.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task RunAsyncWithSameSeedProducesSameTrace(CancellationToken cancellationToken)
    {
        var first = await CaptureRandomTraceAsync(12345, cancellationToken);
        var second = await CaptureRandomTraceAsync(12345, cancellationToken);

        _ = await Assert.That(second).IsEquivalentTo(first, CollectionOrdering.Matching);
    }

    private static async Task<IReadOnlyList<string>> CaptureRandomTraceAsync(int seed, CancellationToken cancellationToken = default)
    {
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                async context =>
                {
                    for (var index = 0; index < 5; index++)
                        context.Fork(async () => await Probe.HitAsync("ready", cancellationToken));

                    await context.JoinAsync(cancellationToken);
                    throw new InvalidOperationException("capture trace");
                },
                new RunOptions { Iterations = 1, Seed = seed },
                cancellationToken));

        return exception.Traces;
    }

    private static async Task<(IReadOnlyList<string> Trace, IReadOnlyList<string> ReleaseOrder)> CaptureScriptedRunAsync(int seed, ReplaySchedule schedule, CancellationToken cancellationToken = default)
    {
        var releases = new List<string>();
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                async context =>
                {
                    for (var index = 0; index < 3; index++)
                        ForkHitReadyAddWorker(context, releases, $"worker-{index + 1}", cancellationToken);

                    await context.JoinAsync(cancellationToken);
                    throw new InvalidOperationException("capture trace");
                },
                new RunOptions { Iterations = 1, Seed = seed, Schedule = schedule },
                cancellationToken));

        return (exception.Traces, releases);
    }
}
