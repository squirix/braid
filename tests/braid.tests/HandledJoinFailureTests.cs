namespace Braid.Tests;

/// <summary>Covers a callback that catches the failure of its join: the run still reports that failure.</summary>
public sealed class HandledJoinFailureTests : TestBase
{
    private const string RealFailure = "real failure in w2";

    /// <summary>Verifies the run reports the worker failure, not the cancellation braid raised in the worker parked before it in fork order.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task HandledWorkerFailureIsReported(CancellationToken cancellationToken)
    {
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                async context =>
                {
                    context.Fork("w1", async () => await Probe.HitAsync("a", cancellationToken));
                    context.Fork("w2", () => FailAfterProbeAsync(new InvalidOperationException(RealFailure), cancellationToken));
                    await JoinAndCatchAsync(context, cancellationToken);
                },
                FailW2WhileW1IsParked(),
                cancellationToken));

        _ = await Assert.That(exception.FailureOrigin).IsEqualTo(RunFailureOrigin.UserTest);
        _ = await Assert.That(exception.InnerException).IsTypeOf<InvalidOperationException>();
        _ = await Assert.That(exception.InnerException!.Message).IsEqualTo(RealFailure);
    }

    /// <summary>Verifies the worker failure is reported whichever worker the seed releases first.</summary>
    /// <param name="seed">The run seed, which decides the worker released first.</param>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    public async Task HandledWorkerFailureIsReportedForSeed(int seed, CancellationToken cancellationToken)
    {
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                async context =>
                {
                    context.Fork("w1", async () => await Probe.HitAsync("a", cancellationToken));
                    context.Fork("w2", () => FailAfterProbeAsync(new InvalidOperationException(RealFailure), cancellationToken));
                    await JoinAndCatchAsync(context, cancellationToken);
                },
                new RunOptions { Iterations = 1, Seed = seed },
                cancellationToken));

        _ = await Assert.That(exception.InnerException).IsTypeOf<InvalidOperationException>();
        _ = await Assert.That(exception.InnerException!.Message).IsEqualTo(RealFailure);
    }

    /// <summary>Verifies a worker that fails while braid stops it does not replace the failure that stopped the run.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task FailureWhileStoppingDoesNotReplaceIt(CancellationToken cancellationToken)
    {
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                async context =>
                {
                    context.Fork(
                        "w1",
                        async () =>
                        {
                            try
                            {
                                await Probe.HitAsync("a", cancellationToken);
                            }
                            catch (OperationCanceledException ex)
                            {
                                throw new InvalidOperationException("consequence in w1", ex);
                            }
                        });
                    context.Fork("w2", () => FailAfterProbeAsync(new InvalidOperationException(RealFailure), cancellationToken));
                    await JoinAndCatchAsync(context, cancellationToken);
                },
                FailW2WhileW1IsParked(),
                cancellationToken));

        _ = await Assert.That(exception.InnerException!.Message).IsEqualTo(RealFailure);
    }

    /// <summary>Verifies a worker that fails with its own cancellation is reported with that cancellation.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task HandledWorkerCancellationIsReported(CancellationToken cancellationToken)
    {
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                async context =>
                {
                    context.Fork("w1", async () => await Probe.HitAsync("a", cancellationToken));
                    context.Fork("w2", () => FailAfterProbeAsync(new OperationCanceledException(RealFailure), cancellationToken));
                    await JoinAndCatchAsync(context, cancellationToken);
                },
                FailW2WhileW1IsParked(),
                cancellationToken));

        _ = await Assert.That(exception.InnerException).IsTypeOf<OperationCanceledException>();
        _ = await Assert.That(exception.InnerException!.Message).IsEqualTo(RealFailure);
    }

    /// <summary>Verifies a handled schedule failure is reported again, not as a failure of the workers braid stopped.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task HandledScheduleFailureIsReported(CancellationToken cancellationToken)
    {
        // The schedule ends after w2 completes, while w1 is still parked at its probe.
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                async context =>
                {
                    context.Fork("w1", async () => await Probe.HitAsync("a", cancellationToken));
                    context.Fork("w2", async () => await Probe.HitAsync("b", cancellationToken));
                    await JoinAndCatchAsync(context, cancellationToken);
                },
                FailW2WhileW1IsParked(),
                cancellationToken));

        _ = await Assert.That(exception.FailureOrigin).IsEqualTo(RunFailureOrigin.Scheduler);
        _ = await Assert.That(exception.Message).Contains("exhausted", StringComparison.Ordinal);
    }

    /// <summary>Verifies a join canceled through its token fails the run with that cancellation after the callback caught it.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task HandledJoinCancellationFailsTheRun(CancellationToken cancellationToken)
    {
        using var joinCts = new CancellationTokenSource();
        var joinToken = joinCts.Token;
        var w2Running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseW2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                async context =>
                {
                    context.Fork("w1", async () => await Probe.HitAsync("a", cancellationToken));
                    context.Fork(
                        "w2",
                        async () =>
                        {
                            await Probe.HitAsync("b", cancellationToken);
                            w2Running.SetResult();
                            await releaseW2.Task.WaitAsync(cancellationToken);
                        });

                    var join = context.JoinAsync(joinToken);
                    await w2Running.Task.WaitAsync(cancellationToken);
                    await joinCts.CancelAsync();
                    releaseW2.SetResult();
                    try
                    {
                        await join;
                    }
                    catch (OperationCanceledException)
                    {
                        // The callback handles the cancellation of its own join.
                    }
                },
                FailW2WhileW1IsParked(),
                cancellationToken));

        _ = await Assert.That(exception.Message).StartsWith("braid run failed.");
        _ = await Assert.That(exception.InnerException).IsAssignableTo<OperationCanceledException>();
    }

    private static RunOptions FailW2WhileW1IsParked() => new() { Iterations = 1, Seed = 1, Schedule = ReplaySchedule.Replay(new ReplayStep("w2", "b")) };

    private static async Task FailAfterProbeAsync(Exception failure, CancellationToken cancellationToken)
    {
        await Probe.HitAsync("b", cancellationToken);
        throw failure;
    }

    private static async Task JoinAndCatchAsync(RunContext context, CancellationToken cancellationToken)
    {
        try
        {
            await context.JoinAsync(cancellationToken);
        }
        catch (RunException)
        {
            // The test expected the failure and handled it.
        }
    }
}
