namespace Braid.Tests;

/// <summary>Covers that every forked worker in a run has a unique id.</summary>
public sealed class WorkerIdTests : TestBase
{
    /// <summary>Verifies a second worker with the same explicit id fails the run.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task DuplicateWorkerIdFailsRun(CancellationToken cancellationToken)
    {
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                static context =>
                {
                    context.Fork("w", static () => Task.CompletedTask);
                    context.Fork("w", static () => Task.CompletedTask);
                    return Task.CompletedTask;
                },
                new RunOptions { Iterations = 1, Seed = 1 },
                cancellationToken));

        _ = await Assert.That(exception.InnerException).IsTypeOf<ArgumentException>();
        _ = await Assert.That(exception.InnerException!.Message).Contains("Worker id 'w' is already used");
    }

    /// <summary>Verifies an explicit id that matches a later generated id fails the run.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task GeneratedIdCollisionFailsRun(CancellationToken cancellationToken)
    {
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                static context =>
                {
                    context.Fork("worker-2", static () => Task.CompletedTask);
                    context.Fork(static () => Task.CompletedTask);
                    return Task.CompletedTask;
                },
                new RunOptions { Iterations = 1, Seed = 1 },
                cancellationToken));

        _ = await Assert.That(exception.InnerException).IsTypeOf<ArgumentException>();
        _ = await Assert.That(exception.InnerException!.Message).Contains("Worker id 'worker-2' is already used");
    }

    /// <summary>Verifies exploration fails on duplicate worker ids instead of merging their probe sequences.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ExploreFailsOnDuplicateWorkerId(CancellationToken cancellationToken)
    {
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.ExploreAsync(
                static options => options.WithSeed(1).WithMaxSchedules(10),
                async braid =>
                {
                    await braid.WorkerAsync("w", async () => await Probe.HitAsync("x", cancellationToken));
                    await braid.WorkerAsync("w", async () => await Probe.HitAsync("y", cancellationToken));
                    await braid.JoinAsync(cancellationToken);
                },
                cancellationToken));

        _ = await Assert.That(exception.FailureOrigin).IsEqualTo(RunFailureOrigin.UserTest);
        _ = await Assert.That(exception.InnerException).IsTypeOf<ArgumentException>();
    }
}
