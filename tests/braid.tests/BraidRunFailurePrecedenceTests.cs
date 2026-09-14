namespace Braid.Tests;

/// <summary>Covers precedence between worker failures, timeouts, external cancellation, and schedule misuse.</summary>
public sealed class BraidRunFailurePrecedenceTests : TestBase
{
    /// <summary>Verifies external cancellation wins over a subsequent worker failure.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ExternalCancellationWinsWhenCanceled(CancellationToken cancellationToken)
    {
        using var runCts = new CancellationTokenSource();
        var runToken = runCts.Token;

        var runTask = Runner.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    await Probe.HitAsync("gate", cancellationToken);

                    while (!runToken.IsCancellationRequested)
                        await Task.Delay(TimeSpan.FromMilliseconds(5), TimeProvider.System, cancellationToken);

                    throw new InvalidOperationException("worker after cancel");
                });

                await context.JoinAsync(runToken);
            },
            new RunOptions { Iterations = 1, Seed = 12345 },
            runToken);

        await Task.Delay(TimeSpan.FromMilliseconds(40), TimeProvider.System, cancellationToken);
        await runCts.CancelAsync();

        var watchdog = Task.Delay(TimeSpan.FromSeconds(2), TimeProvider.System, cancellationToken);
        if (await Task.WhenAny(runTask, watchdog) != runTask)
            Assert.Fail("Braid run did not complete before watchdog timeout.");

        try
        {
            await runTask;
            Assert.Fail("Expected OperationCanceledException.");
        }
        catch (OperationCanceledException)
        {
            // External cancellation wins over the worker failure observed after cancel.
        }
    }

    /// <summary>Verifies an unused schedule with no forked workers fails clearly.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task RunAsyncFailsWhenScheduleNoWorkers(CancellationToken cancellationToken)
    {
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                static _ => Task.CompletedTask,
                new RunOptions
                {
                    Iterations = 1,
                    Seed = 12345,
                    Schedule = ReplaySchedule.Replay(new ReplayStep("worker-1", "ready")),
                },
                cancellationToken));

        _ = await Assert.That(exception.Message).Contains("unused steps", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies forked startup workers are stopped when the callback throws before join.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task RunAsyncStopsStartupOnThrowAfterFork(CancellationToken cancellationToken)
    {
        var runTask = Runner.RunAsync(
            context =>
            {
                context.Fork(async () => await Probe.HitAsync("ready", cancellationToken));

                context.Fork(async () => await Probe.HitAsync("ready", cancellationToken));

                context.Fork(async () => await Probe.HitAsync("ready", cancellationToken));

                throw new InvalidOperationException("callback failed before join");
            },
            new RunOptions { Iterations = 1, Seed = 12345 },
            cancellationToken);

        var watchdog = Task.Delay(TimeSpan.FromSeconds(2), TimeProvider.System, cancellationToken);
        if (await Task.WhenAny(runTask, watchdog) != runTask)
            Assert.Fail("Run should not hang after callback throws.");

        try
        {
            await runTask;
            Assert.Fail("Expected RunException.");
        }
        catch (RunException ex)
        {
            var report = ex.ToString();
            _ = await Assert.That(report).Contains("callback failed before join");
            _ = await Assert.That(report).Contains("worker-1 forked");
            _ = await Assert.That(report).Contains("Trace:");
        }
    }

    /// <summary>Verifies timeout reports include worker and probe trace context.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task TimeoutReportIncludesRunningWorkerTrace(CancellationToken cancellationToken)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            var runTask = Runner.RunAsync(
                async context =>
                {
                    context.Fork(async () =>
                    {
                        await Probe.HitAsync("started", cancellationToken);
                        await gate.Task.WaitAsync(cancellationToken);
                    });

                    await context.JoinAsync(cancellationToken);
                },
                new RunOptions { Iterations = 1, Seed = 12345, Timeout = TimeSpan.FromMilliseconds(50) },
                cancellationToken);

            var watchdog = Task.Delay(TimeSpan.FromSeconds(2), TimeProvider.System, cancellationToken);
            if (await Task.WhenAny(runTask, watchdog) != runTask)
                Assert.Fail("Braid run did not complete before watchdog timeout.");

            try
            {
                await runTask;
                Assert.Fail("Expected RunException.");
            }
            catch (RunException exception)
            {
                var report = exception.ToString();
                _ = await Assert.That(report).Contains("braid run timed out.");
                _ = await Assert.That(report).Contains("started");
                _ = await Assert.That(report).Contains("worker-1");
                _ = await Assert.That(report).Contains("released");
            }
        }
        finally
        {
            _ = gate.TrySetResult();
        }
    }

    /// <summary>Verifies timeout wins when the worker failure happens only after the timeout window.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task TimeoutWinsOverLateWorkerFailure(CancellationToken cancellationToken)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var workerExited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var runTask = Runner.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    try
                    {
                        await Probe.HitAsync("block", cancellationToken);
                        await gate.Task.WaitAsync(cancellationToken);
                        throw new InvalidOperationException("too late after timeout");
                    }
                    finally
                    {
                        _ = workerExited.TrySetResult();
                    }
                });

                await context.JoinAsync(cancellationToken);
            },
            new RunOptions { Iterations = 1, Seed = 12345, Timeout = TimeSpan.FromMilliseconds(50) },
            cancellationToken);

        var watchdog = Task.Delay(TimeSpan.FromSeconds(2), TimeProvider.System, cancellationToken);
        if (await Task.WhenAny(runTask, watchdog) != runTask)
            Assert.Fail("Braid run did not complete before watchdog timeout.");

        try
        {
            await runTask;
            Assert.Fail("Expected RunException.");
        }
        catch (RunException exception)
        {
            _ = gate.TrySetResult();
            await workerExited.Task.WaitAsync(TimeSpan.FromSeconds(2), TimeProvider.System, cancellationToken);
            _ = await Assert.That(exception.Message).Contains("braid run timed out.");
            _ = await Assert.That(exception.ToString()).DoesNotContain("too late after timeout");
        }
    }

    /// <summary>Verifies worker failure is reported when it occurs before the iteration timeout.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task WorkerFailureWinsOverTimeout(CancellationToken cancellationToken)
    {
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                async context =>
                {
                    context.Fork(async () =>
                    {
                        await Probe.HitAsync("before-failure", cancellationToken);
                        throw new InvalidOperationException("worker failed before timeout");
                    });

                    await context.JoinAsync(cancellationToken);
                },
                new RunOptions { Iterations = 1, Seed = 12345, Timeout = TimeSpan.FromSeconds(5) },
                cancellationToken));

        var report = exception.ToString();
        _ = await Assert.That(report).Contains("worker failed before timeout");
        _ = await Assert.That(report).DoesNotContain("braid run timed out.");
    }
}
