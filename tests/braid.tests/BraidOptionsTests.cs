namespace Braid.Tests;

/// <summary>Covers braid options validation behavior.</summary>
public sealed class BraidOptionsTests : TestBase
{
    /// <summary>Verifies the shared default options are valid.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task DefaultOptionsAreValid(CancellationToken cancellationToken)
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

                return context.JoinAsync(cancellationToken);
            },
            RunOptions.Default,
            cancellationToken);

        _ = await Assert.That(executed > 0).IsTrue();
    }

    /// <summary>Verifies null options use the default options.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task RunAsyncAcceptsNullOptions(CancellationToken cancellationToken)
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

                return context.JoinAsync(cancellationToken);
            },
            cancellationToken);

        _ = await Assert.That(executed > 0).IsTrue();
    }

    /// <summary>Verifies negative iterations are rejected before the run body starts.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    [Test]
    public async Task RunAsyncThrowsForNegativeIterations(CancellationToken cancellationToken)
    {
        var executed = 0;

        var exception = BraidAssertions.AssertExpects<ArgumentOutOfRangeException>(() =>
        {
            _ = Runner.RunAsync(
                context =>
                {
                    _ = context;
                    _ = Interlocked.Increment(ref executed);
                    return Task.CompletedTask;
                },
                new RunOptions { Iterations = -1 },
                cancellationToken);
        });

        _ = await Assert.That(exception.ParamName).IsEqualTo(nameof(RunOptions.Iterations));
        _ = await Assert.That(executed).IsEqualTo(0);
    }

    /// <summary>Verifies negative timeout is rejected before the run body starts.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    [Test]
    public async Task RunAsyncThrowsForNegativeTimeout(CancellationToken cancellationToken)
    {
        var executed = 0;

        var exception = BraidAssertions.AssertExpects<ArgumentOutOfRangeException>(() =>
        {
            _ = Runner.RunAsync(
                context =>
                {
                    _ = context;
                    _ = Interlocked.Increment(ref executed);
                    return Task.CompletedTask;
                },
                new RunOptions { Timeout = TimeSpan.FromMilliseconds(-1) },
                cancellationToken);
        });

        _ = await Assert.That(exception.ParamName).IsEqualTo(nameof(RunOptions.Timeout));
        _ = await Assert.That(executed).IsEqualTo(0);
    }

    /// <summary>Verifies zero iterations are rejected before the run body starts.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    [Test]
    public async Task RunAsyncThrowsForZeroIterations(CancellationToken cancellationToken)
    {
        var executed = 0;

        var exception = BraidAssertions.AssertExpects<ArgumentOutOfRangeException>(() =>
        {
            _ = Runner.RunAsync(
                context =>
                {
                    _ = context;
                    _ = Interlocked.Increment(ref executed);
                    return Task.CompletedTask;
                },
                new RunOptions { Iterations = 0 },
                cancellationToken);
        });

        _ = await Assert.That(exception.ParamName).IsEqualTo(nameof(RunOptions.Iterations));
        _ = await Assert.That(executed).IsEqualTo(0);
    }

    /// <summary>Verifies zero timeout is rejected before the run body starts.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    [Test]
    public async Task RunAsyncThrowsForZeroTimeout(CancellationToken cancellationToken)
    {
        var executed = 0;

        var exception = BraidAssertions.AssertExpects<ArgumentOutOfRangeException>(() =>
        {
            _ = Runner.RunAsync(
                context =>
                {
                    _ = context;
                    _ = Interlocked.Increment(ref executed);
                    return Task.CompletedTask;
                },
                new RunOptions { Timeout = TimeSpan.Zero },
                cancellationToken);
        });

        _ = await Assert.That(exception.ParamName).IsEqualTo(nameof(RunOptions.Timeout));
        _ = await Assert.That(executed).IsEqualTo(0);
    }
}
