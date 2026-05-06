using Xunit;

namespace Braid.Tests;

/// <summary>
/// Covers basic braid run behavior.
/// </summary>
public sealed class BraidRunTests : TestBase
{
    /// <summary>
    /// Verifies a run completes after forked operations complete.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncCompletesWhenForksComplete()
    {
        var value = 0;

        await Braid.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    await BraidProbe.HitAsync("first", DefaultCancellationToken);
                    _ = Interlocked.Increment(ref value);
                });

                context.Fork(async () =>
                {
                    await BraidProbe.HitAsync("second", DefaultCancellationToken);
                    _ = Interlocked.Increment(ref value);
                });

                await context.JoinAsync(DefaultCancellationToken);
            },
            new BraidOptions { Iterations = 1, Seed = 12345 },
            DefaultCancellationToken);

        Assert.Equal(2, value);
    }
}
