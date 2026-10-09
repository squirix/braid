namespace Braid.Tests;

/// <summary>Covers API misuse errors under exploration, which must fail exploration as they fail a run.</summary>
public sealed class ExploreMisuseTests : TestBase
{
    /// <summary>Verifies a worker added after JoinAsync fails exploration.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ForkAfterJoinFailsExploration(CancellationToken cancellationToken)
    {
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.ExploreAsync(
                static options => options.WithSeed(1),
                async braid =>
                {
                    await braid.WorkerAsync("w", async () => await Probe.HitAsync("a", cancellationToken));
                    await braid.JoinAsync(cancellationToken);
                    await braid.WorkerAsync("late", static () => Task.CompletedTask);
                },
                cancellationToken));

        _ = await Assert.That(exception.Message).StartsWith("Cannot fork after JoinAsync has started.");
    }

    /// <summary>Verifies a second probe hit while the worker's first probe wait is in flight fails exploration.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ConcurrentProbeHitFailsExploration(CancellationToken cancellationToken)
    {
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.ExploreAsync(
                static options => options.WithSeed(1),
                async braid =>
                {
                    await braid.WorkerAsync(
                        "w",
                        async () =>
                        {
                            var first = Probe.HitAsync("a", cancellationToken).AsTask();
                            await Probe.HitAsync("b", cancellationToken);
                            await first;
                        });
                    await braid.JoinAsync(cancellationToken);
                },
                cancellationToken));

        _ = await Assert.That(exception.InnerException).IsTypeOf<RunException>();
        _ = await Assert.That(exception.InnerException!.Message).StartsWith("Concurrent probe hit on the same worker is not supported.");
    }
}
