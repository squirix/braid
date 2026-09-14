namespace Braid.Tests;

/// <summary>Covers basic braid run behavior.</summary>
public sealed class BraidRunTests : TestBase
{
    /// <summary>Verifies a run completes after forked operations complete.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task RunAsyncCompletesWhenForksComplete(CancellationToken cancellationToken)
    {
        var value = 0;

        await Runner.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    await Probe.HitAsync("first", cancellationToken);
                    _ = Interlocked.Increment(ref value);
                });

                context.Fork(async () =>
                {
                    await Probe.HitAsync("second", cancellationToken);
                    _ = Interlocked.Increment(ref value);
                });

                await context.JoinAsync(cancellationToken);
            },
            new RunOptions { Iterations = 1, Seed = 12345 },
            cancellationToken);

        _ = await Assert.That(value).IsEqualTo(2);
    }

    /// <summary>Verifies all requested iterations run when each iteration passes.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task RunAsyncExecutesAllIterationsWhenPass(CancellationToken cancellationToken)
    {
        var invocations = 0;

        await Runner.RunAsync(
            context =>
            {
                _ = context;
                _ = Interlocked.Increment(ref invocations);
                return Task.CompletedTask;
            },
            new RunOptions { Iterations = 3, Seed = 12345 },
            cancellationToken);

        _ = await Assert.That(invocations).IsEqualTo(3);
    }

    /// <summary>Verifies the failing iteration is reported as a zero-based index.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task RunAsyncReportsFailingIterationIndex(CancellationToken cancellationToken)
    {
        var invocations = 0;

        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                context =>
                {
                    _ = context;
                    var invocation = Interlocked.Increment(ref invocations);
                    return invocation == 2 ? throw new InvalidOperationException("second iteration failed") : Task.CompletedTask;
                },
                new RunOptions { Iterations = 3, Seed = 12345 },
                cancellationToken));

        _ = await Assert.That(exception.Iteration).IsEqualTo(1);
    }

    /// <summary>Verifies a run stops after the first failing iteration.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task RunAsyncStopsAfterFirstFailingIteration(CancellationToken cancellationToken)
    {
        var invocations = 0;

        _ = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                context =>
                {
                    _ = context;
                    var invocation = Interlocked.Increment(ref invocations);
                    return invocation == 2 ? throw new InvalidOperationException("second iteration failed") : Task.CompletedTask;
                },
                new RunOptions { Iterations = 5, Seed = 12345 },
                cancellationToken));

        _ = await Assert.That(invocations).IsEqualTo(2);
    }
}
