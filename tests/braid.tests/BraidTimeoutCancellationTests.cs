namespace Braid.Tests;

/// <summary>Covers timeout and cancellation behavior of the braid scheduler and run reporting.</summary>
public sealed class BraidTimeoutCancellationTests : TestBase
{
    /// <summary>Verifies a canceled probe token does not strand the worker in a permanent wait.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task CanceledProbeDoesNotWaitPermanently(CancellationToken cancellationToken)
    {
        var exception = await RunLocalTokenCanceledProbeAsync(12345, cancellationToken);

        _ = await Assert.That(exception.Message).Contains("A forked operation failed.");
        _ = await Assert.That(exception.InnerException).IsNotNull();
        _ = await Assert.That(exception.InnerException is OperationCanceledException).IsTrue().Because($"Expected cancellation-derived exception, got {exception.InnerException.GetType().FullName}.");
    }

    /// <summary>Verifies cancellation at a probe preserves trace in the failure report.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task CanceledWorkerProbeContainsProbeTrace(CancellationToken cancellationToken)
    {
        var exceptionTask = RunLocalTokenCanceledProbeAsync(12345, cancellationToken);

        var watchdog = Task.Delay(TimeSpan.FromSeconds(5), TimeProvider.System, cancellationToken);
        if (await Task.WhenAny(exceptionTask, watchdog) != exceptionTask)
            Assert.Fail("Braid run did not complete before watchdog timeout.");

        var exception = await exceptionTask;
        var report = exception.ToString();
        _ = await Assert.That(report).Contains("ready");
        _ = await Assert.That(report).Contains("worker-1");
        _ = await Assert.That(report).Contains("Trace:");
    }

    /// <summary>Verifies external cancellation takes precedence over a worker failure observed after cancel.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ExternalCancellationWinsAfterCancel(CancellationToken cancellationToken)
    {
        using var runCts = new CancellationTokenSource();
        var runToken = runCts.Token;
        var workerBlocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var runTask = Runner.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    await Probe.HitAsync("block", cancellationToken);
                    workerBlocked.SetResult();
                    while (!runToken.IsCancellationRequested)
                        await Task.Delay(TimeSpan.FromMilliseconds(5), TimeProvider.System, cancellationToken);

                    throw new InvalidOperationException("after-cancel worker failure");
                });

                await context.JoinAsync(runToken);
            },
            new RunOptions { Iterations = 1, Seed = 12345 },
            runToken);

        var signaled = await Task.WhenAny(workerBlocked.Task, runTask).WaitAsync(cancellationToken);
        if (signaled == runTask)
            await runTask;

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
            // External cancellation takes precedence over the worker failure observed after cancel.
        }
    }

    /// <summary>Verifies worker-local probe cancellation surfaces as worker failure.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ProbeCanceledByLocalTokenAsFailure(CancellationToken cancellationToken)
    {
        var exception = await RunLocalTokenCanceledProbeAsync(36, cancellationToken);

        _ = await Assert.That(exception.InnerException is OperationCanceledException).IsTrue();
        _ = await Assert.That(exception.ToString()).Contains("ready");
    }

    /// <summary>Verifies timeout surfaces as RunException and the run does not hang when StopAsync waits on a non-cooperative worker.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task RunAsyncTimeoutNoHangWorkerIgnores(CancellationToken cancellationToken)
    {
        var unblock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var runTask = Runner.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    await Probe.HitAsync("at-probe", cancellationToken);
                    await unblock.Task.WaitAsync(cancellationToken);
                });

                await context.JoinAsync(cancellationToken);
            },
            new RunOptions { Iterations = 1, Seed = 12345, Timeout = TimeSpan.FromMilliseconds(50) },
            cancellationToken);

        var watchdog = Task.Delay(TimeSpan.FromSeconds(2), TimeProvider.System, cancellationToken);
        var winner = await Task.WhenAny(runTask, watchdog);

        if (winner != runTask)
        {
            _ = unblock.TrySetResult();
            Assert.Fail("Braid run did not complete before watchdog timeout.");
        }

        _ = unblock.TrySetResult();

        try
        {
            await runTask;
            Assert.Fail("Expected RunException for timeout.");
        }
        catch (RunException ex)
        {
            _ = await Assert.That(ex.Message).Contains("braid run timed out.");
        }
    }

    /// <summary>Verifies run cancellation wins over worker-local probe cancellation.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task RunCancellationWinsOverLocalProbeCancel(CancellationToken cancellationToken)
    {
        using var runCts = new CancellationTokenSource();

        var runToken = runCts.Token;
        var workerForked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var runTask = Runner.RunAsync(
            async context =>
            {
                context.Fork(static () => Probe.HitAsync("ready", new CancellationToken(true)).AsTask());

                workerForked.SetResult();
                await context.JoinAsync(runToken);
            },
            new RunOptions
            {
                Iterations = 1,
                Seed = 37,
            },
            runToken);

        await workerForked.Task.WaitAsync(cancellationToken);
        await runCts.CancelAsync();

        var watchdog = Task.Delay(TimeSpan.FromSeconds(2), TimeProvider.System, cancellationToken);
        if (await Task.WhenAny(runTask, watchdog) != runTask)
            Assert.Fail("Braid run did not complete before watchdog timeout.");

        try
        {
            await runTask;
            Assert.Fail("Expected cancellation-related failure.");
        }
        catch (OperationCanceledException)
        {
            // Run cancellation wins directly.
        }
        catch (RunException ex)
        {
            _ = await Assert.That(ex.InnerException is OperationCanceledException).IsTrue();
        }
    }

    /// <summary>Verifies tiny positive timeouts are valid and may deterministically time out.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task VerySmallTimeoutAllowedButMayExpire(CancellationToken cancellationToken)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
                Runner.RunAsync(
                    async context =>
                    {
                        context.Fork(async () => await gate.Task.WaitAsync(cancellationToken));
                        await context.JoinAsync(cancellationToken);
                    },
                    new RunOptions { Iterations = 1, Seed = 35, Timeout = TimeSpan.FromTicks(1) },
                    cancellationToken));

            _ = await Assert.That(exception.Message).Contains("timed out", StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            _ = gate.TrySetResult();
        }
    }

    private static Task<RunException> RunLocalTokenCanceledProbeAsync(int seed, CancellationToken cancellationToken = default) => BraidAssertions.AssertExpectsAsync<RunException>(
        Runner.RunAsync(
            async context =>
            {
                context.Fork(static () => Probe.HitAsync("ready", new CancellationToken(true)).AsTask());
                await context.JoinAsync(cancellationToken);
            },
            new RunOptions { Iterations = 1, Seed = seed, Timeout = TimeSpan.FromSeconds(2) },
            cancellationToken));
}
