using Xunit;

namespace Braid.Examples.UserOperationLimiter;

/// <summary>
/// Demonstrates reproducing and fixing a per-user limiter race.
/// </summary>
public sealed class UserOperationLimiterTests
{
    private static CancellationToken TestCancellationToken => TestContext.Current.CancellationToken;

    /// <summary>
    /// Verifies braid can deterministically reproduce the unsafe limiter race.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task UnsafeLimiterAllowsTwoWorkersAndBraidReportsTheRace()
    {
        var limiter = new UserOperationLimiter("user-1", 1);
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
                        firstAllowed = await limiter.TryEnterAsync(TestCancellationToken);
                    });

                    context.Fork(async () =>
                    {
                        secondAllowed = await limiter.TryEnterAsync(TestCancellationToken);
                    });

                    await context.JoinAsync(TestCancellationToken);
                    Assert.False(firstAllowed && secondAllowed);
                },
                options,
                TestCancellationToken);
        });

        var report = exception.ToString().ReplaceLineEndings("\n");
        const string expectedReportFragment = """
            Seed: 12345
            Iteration: 0
            Schedule:
              1. worker-1 @ after-read
              2. worker-2 @ after-read
              3. worker-1 @ before-write
              4. worker-2 @ before-write
            Replay text:
            hit worker-1 after-read
            hit worker-2 after-read
            hit worker-1 before-write
            hit worker-2 before-write
            Trace:
            """;

        Assert.Contains(expectedReportFragment, report, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the locked limiter survives a deterministic two-worker schedule.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task LockedLimiterAllowsOnlyOneWorkerUnderSameSchedule()
    {
        var limiter = new LockedUserOperationLimiter("user-1", 1);
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
                    firstAllowed = await limiter.TryEnterAsync(TestCancellationToken);
                });

                context.Fork(async () =>
                {
                    secondAllowed = await limiter.TryEnterAsync(TestCancellationToken);
                });

                return context.JoinAsync(TestCancellationToken);
            },
            options,
            TestCancellationToken);

        Assert.True(firstAllowed ^ secondAllowed);

        limiter.Exit();
        Assert.True(await limiter.TryEnterAsync(TestCancellationToken));
    }

    /// <summary>
    /// Verifies unsafe limiter constructor validation.
    /// </summary>
    [Fact]
    public void UnsafeLimiterRejectsInvalidConstructorArguments()
    {
        _ = Assert.Throws<ArgumentException>(static () => new UserOperationLimiter(" ", 1));
        _ = Assert.Throws<ArgumentOutOfRangeException>(static () => new UserOperationLimiter("user-1", 0));
        _ = Assert.Throws<ArgumentOutOfRangeException>(static () => new UserOperationLimiter("user-1", -1));
    }

    /// <summary>
    /// Verifies locked limiter constructor validation.
    /// </summary>
    [Fact]
    public void LockedLimiterRejectsInvalidConstructorArguments()
    {
        _ = Assert.Throws<ArgumentException>(static () => new LockedUserOperationLimiter(" ", 1));
        _ = Assert.Throws<ArgumentOutOfRangeException>(static () => new LockedUserOperationLimiter("user-1", 0));
        _ = Assert.Throws<ArgumentOutOfRangeException>(static () => new LockedUserOperationLimiter("user-1", -1));
    }
}
