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

    /// <summary>
    /// Verifies all requested iterations run when each iteration passes.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncExecutesAllIterationsWhenTheyPass()
    {
        var invocations = 0;

        await Braid.RunAsync(
            context =>
            {
                _ = context;
                _ = Interlocked.Increment(ref invocations);
                return Task.CompletedTask;
            },
            new BraidOptions { Iterations = 3, Seed = 12345 },
            DefaultCancellationToken);

        Assert.Equal(3, invocations);
    }

    /// <summary>
    /// Verifies the failing iteration is reported as a zero-based index.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncReportsFailingIterationAsZeroBasedIndex()
    {
        var invocations = 0;

        var exception = await Assert.ThrowsAsync<BraidRunException>(async () =>
        {
            await Braid.RunAsync(
                context =>
                {
                    _ = context;
                    var invocation = Interlocked.Increment(ref invocations);
                    return invocation == 2 ? throw new InvalidOperationException("second iteration failed") : Task.CompletedTask;
                },
                new BraidOptions { Iterations = 3, Seed = 12345 },
                DefaultCancellationToken);
        });

        Assert.Equal(1, exception.Iteration);
    }

    /// <summary>
    /// Verifies a run stops after the first failing iteration.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncStopsAfterFirstFailingIteration()
    {
        var invocations = 0;

        _ = await Assert.ThrowsAsync<BraidRunException>(async () =>
        {
            await Braid.RunAsync(
                context =>
                {
                    _ = context;
                    var invocation = Interlocked.Increment(ref invocations);
                    return invocation == 2 ? throw new InvalidOperationException("second iteration failed") : Task.CompletedTask;
                },
                new BraidOptions { Iterations = 5, Seed = 12345 },
                DefaultCancellationToken);
        });

        Assert.Equal(2, invocations);
    }
}
