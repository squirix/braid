using Xunit;

namespace Braid.Tests;

/// <summary>Covers timeout and cancellation behavior of the braid scheduler and run reporting.</summary>
public sealed class BraidTimeoutCancellationTests : TestBase
{
    /// <summary>Verifies a canceled probe token does not strand the worker in a permanent wait.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task CanceledProbeDoesNotWaitPermanently()
    {
        var exception = await RunLocalTokenCanceledProbeAsync(12345);

        Assert.Contains("A forked operation failed.", exception.Message, StringComparison.Ordinal);
        Assert.NotNull(exception.InnerException);
        Assert.True(exception.InnerException is OperationCanceledException, $"Expected cancellation-derived exception, got {exception.InnerException.GetType().FullName}.");
    }

    /// <summary>Verifies cancellation at a probe preserves trace in the failure report.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task CanceledWorkerProbeContainsProbeTrace()
    {
        var exceptionTask = RunLocalTokenCanceledProbeAsync(12345);

        var watchdog = Task.Delay(TimeSpan.FromSeconds(2), TimeProvider.System, DefaultCancellationToken);
        if (await Task.WhenAny(exceptionTask, watchdog) != exceptionTask)
            Assert.Fail("Braid run did not complete before watchdog timeout.");

        var exception = await exceptionTask;
        var report = exception.ToString();
        Assert.Contains("ready", report, StringComparison.Ordinal);
        Assert.Contains("worker-1", report, StringComparison.Ordinal);
        Assert.Contains("Trace:", report, StringComparison.Ordinal);
    }

    /// <summary>Verifies external cancellation takes precedence over a worker failure observed after cancel.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ExternalCancellationWinsAfterCancel()
    {
        using var runCts = new CancellationTokenSource();
        var runToken = runCts.Token;

        var runTask = BraidRunner.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    await BraidProbe.HitAsync("block", DefaultCancellationToken);
                    while (!runToken.IsCancellationRequested)
                        await Task.Delay(TimeSpan.FromMilliseconds(5), TimeProvider.System, DefaultCancellationToken);

                    throw new InvalidOperationException("after-cancel worker failure");
                });

                await context.JoinAsync(runToken);
            },
            new BraidOptions { Iterations = 1, Seed = 12345 },
            runToken);

        await Task.Delay(TimeSpan.FromMilliseconds(50), TimeProvider.System, DefaultCancellationToken);
        await runCts.CancelAsync();

        var watchdog = Task.Delay(TimeSpan.FromSeconds(2), TimeProvider.System, DefaultCancellationToken);
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
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ProbeCanceledByLocalTokenAsFailure()
    {
        var exception = await RunLocalTokenCanceledProbeAsync(36);

        Assert.True(exception.InnerException is OperationCanceledException);
        Assert.Contains("ready", exception.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Verifies timeout surfaces as BraidRunException and the run does not hang when StopAsync waits on a non-cooperative worker.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncTimeoutNoHangWorkerIgnores()
    {
        var unblock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var runTask = BraidRunner.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    await BraidProbe.HitAsync("at-probe", DefaultCancellationToken);
                    await unblock.Task.WaitAsync(DefaultCancellationToken);
                });

                await context.JoinAsync(DefaultCancellationToken);
            },
            new BraidOptions { Iterations = 1, Seed = 12345, Timeout = TimeSpan.FromMilliseconds(50) },
            DefaultCancellationToken);

        var watchdog = Task.Delay(TimeSpan.FromSeconds(2), TimeProvider.System, DefaultCancellationToken);
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
            Assert.Fail("Expected BraidRunException for timeout.");
        }
        catch (BraidRunException ex)
        {
            Assert.Contains("braid run timed out.", ex.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>Verifies run cancellation wins over worker-local probe cancellation.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunCancellationWinsOverLocalProbeCancel()
    {
        using var runCts = new CancellationTokenSource();

        var runToken = runCts.Token;
        var workerForked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var runTask = BraidRunner.RunAsync(
            async context =>
            {
                context.Fork(static () => BraidProbe.HitAsync("ready", new CancellationToken(true)).AsTask());

                workerForked.SetResult();
                await context.JoinAsync(runToken);
            },
            new BraidOptions
            {
                Iterations = 1,
                Seed = 37,
            },
            runToken);

        await workerForked.Task.WaitAsync(DefaultCancellationToken);
        await runCts.CancelAsync();

        var watchdog = Task.Delay(TimeSpan.FromSeconds(2), TimeProvider.System, DefaultCancellationToken);
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
        catch (BraidRunException ex)
        {
            Assert.True(ex.InnerException is OperationCanceledException);
        }
    }

    /// <summary>Verifies tiny positive timeouts are valid and may deterministically time out.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task VerySmallTimeoutAllowedButMayExpire()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var exception = await Assert.ThrowsAsync<BraidRunException>(async () =>
            {
                await BraidRunner.RunAsync(
                    async context =>
                    {
                        context.Fork(async () => await gate.Task.WaitAsync(DefaultCancellationToken));
                        await context.JoinAsync(DefaultCancellationToken);
                    },
                    new BraidOptions { Iterations = 1, Seed = 35, Timeout = TimeSpan.FromTicks(1) },
                    DefaultCancellationToken);
            });

            Assert.Contains("timed out", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            _ = gate.TrySetResult();
        }
    }

    private static Task<BraidRunException> RunLocalTokenCanceledProbeAsync(int seed) =>
        Assert.ThrowsAsync<BraidRunException>(async () =>
        {
            await BraidRunner.RunAsync(
                static async context =>
                {
                    context.Fork(static () => BraidProbe.HitAsync("ready", new CancellationToken(true)).AsTask());
                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions { Iterations = 1, Seed = seed, Timeout = TimeSpan.FromSeconds(2) },
                DefaultCancellationToken);
        });
}
