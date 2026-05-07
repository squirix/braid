using Xunit;

namespace Braid.Examples.UserOperationLimiter;

/// <summary>
/// Demonstrates reproducing and fixing a per-user limiter race.
/// </summary>
public sealed class UserOperationLimiterTests
{
    private static readonly CancellationToken CancellationToken = TestContext.Current.CancellationToken;

    /// <summary>
    /// Verifies braid can deterministically reproduce the unsafe limiter race.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task BraidReproducesLimiterRace()
    {
        var limiter = new UserOperationLimiter();
        var firstAllowed = false;
        var secondAllowed = false;
        var options = new BraidOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = BraidSchedule.Replay(
                new BraidStep("worker-1", "after-read"),
                new BraidStep("worker-2", "after-read"),
                new BraidStep("worker-1", "before-write"),
                new BraidStep("worker-2", "before-write")),
        };

        var exception = await Assert.ThrowsAsync<BraidRunException>(async () =>
        {
            await Braid.RunAsync(
                async context =>
                {
                    context.Fork(async () =>
                    {
                        firstAllowed = await limiter.TryEnterAsync("user-1", 1, CancellationToken);
                    });

                    context.Fork(async () =>
                    {
                        secondAllowed = await limiter.TryEnterAsync("user-1", 1, CancellationToken);
                    });

                    await context.JoinAsync(CancellationToken);
                    Assert.False(firstAllowed && secondAllowed);
                },
                options,
                CancellationToken);
        });

        var report = exception.ToString();
        Assert.Contains("Seed: 12345", report, StringComparison.Ordinal);
        Assert.Contains("Schedule:", report, StringComparison.Ordinal);
        Assert.Contains("Trace:", report, StringComparison.Ordinal);
        Assert.Contains("worker-1", report, StringComparison.Ordinal);
        Assert.Contains("worker-2", report, StringComparison.Ordinal);
        Assert.Contains("after-read", report, StringComparison.Ordinal);
        Assert.Contains("before-write", report, StringComparison.Ordinal);

        limiter.Exit("user-1");
        Assert.True(await limiter.TryEnterAsync("user-1", 1, CancellationToken));
    }

    /// <summary>
    /// Verifies the locked limiter survives a deterministic two-worker schedule.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task BraidScheduleDoesNotBreakLockedLimiter()
    {
        var limiter = new LockedUserOperationLimiter();
        var firstAllowed = false;
        var secondAllowed = false;
        var options = new BraidOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = BraidSchedule.Replay(
                new BraidStep("worker-1", "before-enter"),
                new BraidStep("worker-2", "before-enter"),
                new BraidStep("worker-1", "after-enter"),
                new BraidStep("worker-2", "after-enter")),
        };

        await Braid.RunAsync(
            context =>
            {
                context.Fork(async () =>
                {
                    firstAllowed = await limiter.TryEnterAsync("user-1", 1, CancellationToken);
                });

                context.Fork(async () =>
                {
                    secondAllowed = await limiter.TryEnterAsync("user-1", 1, CancellationToken);
                });

                return context.JoinAsync(CancellationToken);
            },
            options,
            CancellationToken);

        Assert.True(firstAllowed ^ secondAllowed);

        limiter.Exit("user-1");
        Assert.True(await limiter.TryEnterAsync("user-1", 1, CancellationToken));
    }
}
