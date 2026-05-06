using Xunit;

namespace Braid.Tests;

public sealed class BraidTests : TestBase
{
    [Fact]
    public async Task RunAsyncCompletesWhenForksComplete()
    {
        var value = 0;

        await Braid.RunAsync(async context =>
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
            await Braid.RunAsync(static async context =>
            {
                context.Fork(static async () =>
                {
                    await BraidProbe.HitAsync("before-failure", DefaultCancellationToken);
                    throw new InvalidOperationException("boom");
                });

                await context.JoinAsync(DefaultCancellationToken);
            }, new BraidOptions { Iterations = 1, Seed = 12345 }, DefaultCancellationToken);
        });

        Assert.Equal(12345, exception.Seed);
        Assert.Contains(exception.Trace, static line => line.Contains("before-failure", StringComparison.Ordinal));
        Assert.Contains("boom", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsyncReplaysScriptedScheduleThatReproducesLostUpdate()
    {
        var options = new BraidOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule =
            [
                new BraidScheduleStep("worker-1", "after-read"),
                new BraidScheduleStep("worker-2", "after-read"),
                new BraidScheduleStep("worker-1", "before-write"),
                new BraidScheduleStep("worker-2", "before-write"),
            ],
        };

        var exception = await Assert.ThrowsAsync<BraidRunException>(async () =>
        {
            await Braid.RunAsync(static async context =>
            {
                var value = 0;

                context.Fork(async () =>
                {
                    var current = value;
                    await BraidProbe.HitAsync("after-read", DefaultCancellationToken);
                    await BraidProbe.HitAsync("before-write", DefaultCancellationToken);
                    value = current + 1;
                });

                context.Fork(async () =>
                {
                    var current = value;
                    await BraidProbe.HitAsync("after-read", DefaultCancellationToken);
                    await BraidProbe.HitAsync("before-write", DefaultCancellationToken);
                    value = current + 1;
                });

                await context.JoinAsync(DefaultCancellationToken);

                Assert.Equal(2, value);
            }, options, DefaultCancellationToken);
        });

        Assert.Equal(12345, exception.Seed);
        Assert.Contains(exception.Trace, static line => line.Contains("worker-1 hit after-read", StringComparison.Ordinal));
        Assert.Contains(exception.Trace, static line => line.Contains("worker-2 hit before-write", StringComparison.Ordinal));
    }
}
