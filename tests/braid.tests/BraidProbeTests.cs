namespace Braid.Tests;

/// <summary>Covers explicit probe behavior.</summary>
public sealed class BraidProbeTests : TestBase
{
    /// <summary>Verifies probe behavior does not leak outside a failed run.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task HitAsyncOutsideRunCompletesFailedRun(CancellationToken cancellationToken)
    {
        var operation = Runner.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    await Probe.HitAsync("before-failure", cancellationToken);
                    throw new InvalidOperationException("scope-failure");
                });

                await context.JoinAsync(cancellationToken);
            },
            new RunOptions { Iterations = 1, Seed = 12345 },
            cancellationToken);

        _ = await BraidAssertions.AssertExpectsAsync<RunException>(operation);

        await Probe.HitAsync("outside-run", cancellationToken);
    }

    /// <summary>Verifies probes are no-ops outside a braid run.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public Task HitAsyncOutsideRunCompletesImmediately(CancellationToken cancellationToken) => AssertProbeIsNoOpOutsideRunAsync(cancellationToken);
}
