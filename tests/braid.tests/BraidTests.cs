using Xunit;
using BraidRunner = Braid.Braid;

namespace Braid.Tests;

public sealed class BraidTests : TestBase
{
    [Fact]
    public async Task RunAsyncCompletesWhenForksComplete()
    {
        var value = 0;

        await BraidRunner.RunAsync(async context =>
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
        }, new BraidOptions { Iterations = 1, Seed = 12345 }, DefaultCancellationToken);

        Assert.Equal(2, value);
    }

    [Fact]
    public async Task RunAsyncReportsSeedAndTraceWhenInterleavingFails()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await BraidRunner.RunAsync(static async context =>
            {
                var value = 0;

                context.Fork(async () =>
                {
                    var current = value;
                    await BraidProbe.HitAsync("after-read-a", DefaultCancellationToken);
                    value = current + 1;
                });

                context.Fork(async () =>
                {
                    var current = value;
                    await BraidProbe.HitAsync("after-read-b", DefaultCancellationToken);
                    value = current + 1;
                });

                await context.JoinAsync(DefaultCancellationToken);

                Assert.Equal(2, value);
            }, new BraidOptions { Iterations = 1, Seed = 12345 }, DefaultCancellationToken);
        });

        Assert.Equal(12345, exception.Seed);
        Assert.Contains(exception.Trace, static line => line.Contains("after-read", StringComparison.Ordinal));
    }
}
