using Xunit;

namespace Braid.Tests;

/// <summary>Covers explicit probe behavior.</summary>
public sealed class BraidProbeTests : TestBase
{
    /// <summary>Verifies probe behavior does not leak outside a failed run.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task HitAsyncOutsideRunCompletesFailedRun()
    {
        var operation = Runner.RunAsync(
            static async context =>
            {
                context.Fork(static async () =>
                {
                    await Probe.HitAsync("before-failure", DefaultCancellationToken);
                    throw new InvalidOperationException("scope-failure");
                });

                await context.JoinAsync(DefaultCancellationToken);
            },
            new RunOptions { Iterations = 1, Seed = 12345 },
            DefaultCancellationToken);

        _ = await Assertions.ExpectsAsync<RunException>(operation);

        await Probe.HitAsync("outside-run", DefaultCancellationToken);
    }

    /// <summary>Verifies probes are no-ops outside a braid run.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public Task HitAsyncOutsideRunCompletesImmediately() => AssertProbeIsNoOpOutsideRunAsync();
}
