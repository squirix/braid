using Xunit;

namespace Braid.Tests;

/// <summary>
/// Covers explicit probe behavior.
/// </summary>
public sealed class BraidProbeTests : TestBase
{
    /// <summary>
    /// Verifies probes are no-ops outside a braid run.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task HitAsyncOutsideRunCompletesImmediately() => await BraidProbe.HitAsync("outside-run", DefaultCancellationToken);

    /// <summary>
    /// Verifies probe behavior does not leak outside a failed run.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task HitAsyncOutsideRunStillCompletesAfterFailedRun()
    {
        _ = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await Braid.RunAsync(
                static async context =>
                {
                    context.Fork(static async () =>
                    {
                        await BraidProbe.HitAsync("before-failure", DefaultCancellationToken);
                        throw new InvalidOperationException("scope-failure");
                    });

                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions { Iterations = 1, Seed = 12345 },
                DefaultCancellationToken);
        });

        await BraidProbe.HitAsync("outside-run", DefaultCancellationToken);
    }
}
