using Xunit;

namespace Braid.Tests;

/// <summary>Covers braid options validation behavior.</summary>
public sealed class BraidOptionsTests : TestBase
{
    /// <summary>Verifies the shared default options are valid.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task DefaultOptionsAreValid()
    {
        var executed = 0;

        await Runner.RunAsync(
            context =>
            {
                context.Fork(() =>
                {
                    _ = Interlocked.Increment(ref executed);
                    return Task.CompletedTask;
                });

                return context.JoinAsync(DefaultCancellationToken);
            },
            RunOptions.Default,
            DefaultCancellationToken);

        Assert.True(executed > 0);
    }

    /// <summary>Verifies null options use the default options.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncAcceptsNullOptions()
    {
        var executed = 0;

        await Runner.RunAsync(
            context =>
            {
                context.Fork(() =>
                {
                    _ = Interlocked.Increment(ref executed);
                    return Task.CompletedTask;
                });

                return context.JoinAsync(DefaultCancellationToken);
            },
            DefaultCancellationToken);

        Assert.True(executed > 0);
    }

    /// <summary>Verifies negative iterations are rejected before the run body starts.</summary>
    [Fact]
    public void RunAsyncThrowsForNegativeIterations()
    {
        var executed = 0;

        var exception = Assertions.Expects<ArgumentOutOfRangeException>(() =>
        {
            _ = Runner.RunAsync(
                context =>
                {
                    _ = context;
                    _ = Interlocked.Increment(ref executed);
                    return Task.CompletedTask;
                },
                new RunOptions { Iterations = -1 },
                DefaultCancellationToken);
        });

        Assert.Equal(nameof(RunOptions.Iterations), exception.ParamName);
        Assert.Equal(0, executed);
    }

    /// <summary>Verifies negative timeout is rejected before the run body starts.</summary>
    [Fact]
    public void RunAsyncThrowsForNegativeTimeout()
    {
        var executed = 0;

        var exception = Assertions.Expects<ArgumentOutOfRangeException>(() =>
        {
            _ = Runner.RunAsync(
                context =>
                {
                    _ = context;
                    _ = Interlocked.Increment(ref executed);
                    return Task.CompletedTask;
                },
                new RunOptions { Timeout = TimeSpan.FromMilliseconds(-1) },
                DefaultCancellationToken);
        });

        Assert.Equal(nameof(RunOptions.Timeout), exception.ParamName);
        Assert.Equal(0, executed);
    }

    /// <summary>Verifies zero iterations are rejected before the run body starts.</summary>
    [Fact]
    public void RunAsyncThrowsForZeroIterations()
    {
        var executed = 0;

        var exception = Assertions.Expects<ArgumentOutOfRangeException>(() =>
        {
            _ = Runner.RunAsync(
                context =>
                {
                    _ = context;
                    _ = Interlocked.Increment(ref executed);
                    return Task.CompletedTask;
                },
                new RunOptions { Iterations = 0 },
                DefaultCancellationToken);
        });

        Assert.Equal(nameof(RunOptions.Iterations), exception.ParamName);
        Assert.Equal(0, executed);
    }

    /// <summary>Verifies zero timeout is rejected before the run body starts.</summary>
    [Fact]
    public void RunAsyncThrowsForZeroTimeout()
    {
        var executed = 0;

        var exception = Assertions.Expects<ArgumentOutOfRangeException>(() =>
        {
            _ = Runner.RunAsync(
                context =>
                {
                    _ = context;
                    _ = Interlocked.Increment(ref executed);
                    return Task.CompletedTask;
                },
                new RunOptions { Timeout = TimeSpan.Zero },
                DefaultCancellationToken);
        });

        Assert.Equal(nameof(RunOptions.Timeout), exception.ParamName);
        Assert.Equal(0, executed);
    }
}
