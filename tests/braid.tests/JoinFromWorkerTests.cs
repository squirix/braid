namespace Braid.Tests;

/// <summary>Covers JoinAsync called from inside a forked worker.</summary>
public sealed class JoinFromWorkerTests : TestBase
{
    /// <summary>Verifies a join from a worker fails at once instead of waiting for the run timeout.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task JoinFromWorkerFailsAtOnce(CancellationToken cancellationToken)
    {
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                context =>
                {
                    context.Fork(
                        "w",
                        async () =>
                        {
                            await Probe.HitAsync("a", cancellationToken);
                            await context.JoinAsync(cancellationToken);
                        });
                    return context.JoinAsync(cancellationToken);
                },
                new RunOptions { Iterations = 1, Seed = 1, Timeout = TimeSpan.FromMinutes(1) },
                cancellationToken));

        _ = await Assert.That(exception.FailureOrigin).IsEqualTo(RunFailureOrigin.UserTest);
        _ = await Assert.That(exception.InnerException).IsTypeOf<InvalidOperationException>();
        _ = await Assert.That(exception.InnerException!.Message).StartsWith("JoinAsync cannot be called from a forked worker");
    }

    /// <summary>Verifies exploration fails when a worker joins.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ExploreFailsOnJoinFromWorker(CancellationToken cancellationToken)
    {
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.ExploreAsync(
                static options => options.WithTimeout(TimeSpan.FromMinutes(1)),
                async braid =>
                {
                    await braid.WorkerAsync("w", async () => await braid.JoinAsync(cancellationToken));
                    await braid.JoinAsync(cancellationToken);
                },
                cancellationToken));

        _ = await Assert.That(exception.FailureOrigin).IsEqualTo(RunFailureOrigin.UserTest);
        _ = await Assert.That(exception.InnerException).IsTypeOf<InvalidOperationException>();
        _ = await Assert.That(exception.InnerException!.Message).StartsWith("JoinAsync cannot be called from a forked worker");
    }

    /// <summary>Verifies a join from a task that the callback started is still allowed.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task JoinFromCallbackTaskSucceeds(CancellationToken cancellationToken)
    {
        var completed = 0;
        await Runner.RunAsync(
            context =>
            {
                context.Fork(
                    "w",
                    async () =>
                    {
                        await Probe.HitAsync("a", cancellationToken);
                        _ = Interlocked.Increment(ref completed);
                    });
                return StartNewOnThreadPoolAsync(() => context.JoinAsync(cancellationToken), cancellationToken);
            },
            new RunOptions { Iterations = 1, Seed = 1 },
            cancellationToken);

        _ = await Assert.That(completed).IsEqualTo(1);
    }
}
