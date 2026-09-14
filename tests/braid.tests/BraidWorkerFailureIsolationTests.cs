namespace Braid.Tests;

/// <summary>Covers isolation of worker failures so they are reported rather than masked by siblings.</summary>
public sealed class BraidWorkerFailureIsolationTests : TestBase
{
    /// <summary>Verifies multiple worker failures report one failure while trace still records both workers.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task MultipleWorkersOneFailureBothTraced(CancellationToken cancellationToken)
    {
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                async context =>
                {
                    context.Fork(async () =>
                    {
                        await Probe.HitAsync("first", cancellationToken);
                        throw new InvalidOperationException("worker one failed");
                    });

                    context.Fork(async () =>
                    {
                        await Probe.HitAsync("second", cancellationToken);
                        throw new InvalidOperationException("worker two failed");
                    });

                    await context.JoinAsync(cancellationToken);
                },
                new RunOptions
                {
                    Iterations = 1,
                    Seed = 5103,
                    Schedule = ReplaySchedule.Replay(new ReplayStep("worker-1", "first"), new ReplayStep("worker-2", "second")),
                },
                cancellationToken));

        var report = exception.ToString();
        _ = await Assert.That(report.Contains("worker one failed", StringComparison.Ordinal) || report.Contains("worker two failed", StringComparison.Ordinal)).IsTrue().Because("Expected at least one worker failure message in report.");
        _ = await Assert.That(report).Contains("worker-1");
        _ = await Assert.That(report).Contains("worker-2");
    }

    /// <summary>Verifies synchronously completing worker trace includes fork/startup release/complete.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task SynchronouslyCompletingWorkerFullTrace(CancellationToken cancellationToken)
    {
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                async context =>
                {
                    context.Fork(static () => Task.CompletedTask);
                    await context.JoinAsync(cancellationToken);
                    throw new InvalidOperationException("fail-after-join");
                },
                new RunOptions { Iterations = 1, Seed = 5106 },
                cancellationToken));

        var report = exception.ToString();
        _ = await Assert.That(report).Contains("worker-1 forked");
        _ = await Assert.That(report).Contains("worker-1 released");
        _ = await Assert.That(report).Contains("worker-1 completed");
        _ = await Assert.That(report).DoesNotContain("released at");
    }

    /// <summary>Verifies synchronously throwing fork delegates are reported clearly.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task SynchronouslyThrowingWorkerReported(CancellationToken cancellationToken)
    {
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                async context =>
                {
                    context.Fork(static () => throw new InvalidOperationException("sync throw"));
                    await context.JoinAsync(cancellationToken);
                },
                new RunOptions { Iterations = 1, Seed = 5107 },
                cancellationToken));

        var report = exception.ToString();
        _ = await Assert.That(report).Contains("sync throw");
        _ = await Assert.That(report).Contains("worker-1 forked");
        _ = await Assert.That(report).Contains("worker-1 completed");
    }

    /// <summary>Verifies primary worker failures are not masked by non-cooperative sibling workers.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task WorkerFailureNotMaskedDuringStop(CancellationToken cancellationToken)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var exceptionTask = BraidAssertions.AssertExpectsAsync<RunException>(
                Runner.RunAsync(
                    async context =>
                    {
                        context.Fork(static () => Task.FromException(new InvalidOperationException("primary worker failure")));
                        context.Fork(async () =>
                        {
                            await Probe.HitAsync("waiter", cancellationToken);
                            await gate.Task.WaitAsync(cancellationToken);
                        });

                        await context.JoinAsync(cancellationToken);
                    },
                    new RunOptions { Iterations = 1, Seed = 5102 },
                    cancellationToken));

            await AssertCompletesBeforeWatchdogAsync(exceptionTask, "Worker failure should not be masked by stop path.", TimeSpan.FromSeconds(3), false, cancellationToken);
            var exception = await exceptionTask;
            _ = await Assert.That(exception.ToString()).Contains("primary worker failure");
        }
        finally
        {
            _ = gate.TrySetResult();
        }
    }

    /// <summary>Verifies worker failure while sibling waits at probe stops sibling cleanly.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task WorkerFailureStopsWaitingSiblingCleanly(CancellationToken cancellationToken)
    {
        var exceptionTask = BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                async context =>
                {
                    context.Fork(async () =>
                    {
                        await Probe.HitAsync("fail-ready", cancellationToken);
                        throw new InvalidOperationException("failing worker");
                    });

                    context.Fork(async () => await Probe.HitAsync("blocked", cancellationToken));

                    await context.JoinAsync(cancellationToken);
                },
                new RunOptions
                {
                    Iterations = 1,
                    Seed = 5104,
                    Schedule = ReplaySchedule.Replay(new ReplayStep("worker-1", "fail-ready"), new ReplayStep("worker-2", "blocked")),
                },
                cancellationToken));

        await AssertCompletesBeforeWatchdogAsync(exceptionTask, "Run should fail without deadlock.", TimeSpan.FromSeconds(3), false, cancellationToken);
        var exception = await exceptionTask;
        var report = exception.ToString();
        _ = await Assert.That(report).Contains("failing worker");
        _ = await Assert.That(report).Contains("worker-2 hit blocked");
    }
}
