using Xunit;

namespace Braid.Tests;

/// <summary>
/// Covers braid options validation behavior.
/// </summary>
public sealed class BraidOptionsTests : TestBase
{
    /// <summary>
    /// Verifies zero iterations are rejected before the run body starts.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncThrowsForZeroIterations()
    {
        var executed = 0;

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
        {
            await Braid.RunAsync(
                context =>
                {
                    _ = context;
                    _ = Interlocked.Increment(ref executed);
                    return Task.CompletedTask;
                },
                new BraidOptions { Iterations = 0 },
                DefaultCancellationToken);
        });

        Assert.Equal(nameof(BraidOptions.Iterations), exception.ParamName);
        Assert.Equal(0, executed);
    }

    /// <summary>
    /// Verifies negative iterations are rejected before the run body starts.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncThrowsForNegativeIterations()
    {
        var executed = 0;

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
        {
            await Braid.RunAsync(
                context =>
                {
                    _ = context;
                    _ = Interlocked.Increment(ref executed);
                    return Task.CompletedTask;
                },
                new BraidOptions { Iterations = -1 },
                DefaultCancellationToken);
        });

        Assert.Equal(nameof(BraidOptions.Iterations), exception.ParamName);
        Assert.Equal(0, executed);
    }

    /// <summary>
    /// Verifies zero timeout is rejected before the run body starts.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncThrowsForZeroTimeout()
    {
        var executed = 0;

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
        {
            await Braid.RunAsync(
                context =>
                {
                    _ = context;
                    _ = Interlocked.Increment(ref executed);
                    return Task.CompletedTask;
                },
                new BraidOptions { Timeout = TimeSpan.Zero },
                DefaultCancellationToken);
        });

        Assert.Equal(nameof(BraidOptions.Timeout), exception.ParamName);
        Assert.Equal(0, executed);
    }

    /// <summary>
    /// Verifies negative timeout is rejected before the run body starts.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncThrowsForNegativeTimeout()
    {
        var executed = 0;

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
        {
            await Braid.RunAsync(
                context =>
                {
                    _ = context;
                    _ = Interlocked.Increment(ref executed);
                    return Task.CompletedTask;
                },
                new BraidOptions { Timeout = TimeSpan.FromMilliseconds(-1) },
                DefaultCancellationToken);
        });

        Assert.Equal(nameof(BraidOptions.Timeout), exception.ParamName);
        Assert.Equal(0, executed);
    }

    /// <summary>
    /// Verifies null options use the default options.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncAcceptsNullOptions()
    {
        var executed = 0;

        await Braid.RunAsync(
            context =>
            {
                context.Fork(() =>
                {
                    _ = Interlocked.Increment(ref executed);
                    return Task.CompletedTask;
                });

                return context.JoinAsync(DefaultCancellationToken);
            },
            cancellationToken: DefaultCancellationToken);

        Assert.True(executed > 0);
    }

    /// <summary>
    /// Verifies the shared default options are valid.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task DefaultOptionsAreValid()
    {
        var executed = 0;

        await Braid.RunAsync(
            context =>
            {
                context.Fork(() =>
                {
                    _ = Interlocked.Increment(ref executed);
                    return Task.CompletedTask;
                });

                return context.JoinAsync(DefaultCancellationToken);
            },
            BraidOptions.Default,
            DefaultCancellationToken);

        Assert.True(executed > 0);
    }
}
