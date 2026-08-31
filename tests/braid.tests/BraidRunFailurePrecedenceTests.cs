using Xunit;

namespace Braid.Tests;

/// <summary>Covers precedence between worker failures, timeouts, external cancellation, and schedule misuse.</summary>
public sealed class BraidRunFailurePrecedenceTests : TestBase
{
    /// <summary>Verifies external cancellation wins over a subsequent worker failure.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ExternalCancellationWinsWhenCanceled()
    {
        using var runCts = new CancellationTokenSource();
        var runToken = runCts.Token;

        var runTask = BraidRunner.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    await BraidProbe.HitAsync("gate", DefaultCancellationToken);

                    while (!runToken.IsCancellationRequested)
                        await Task.Delay(TimeSpan.FromMilliseconds(5), TimeProvider.System, DefaultCancellationToken);

                    throw new InvalidOperationException("worker after cancel");
                });

                await context.JoinAsync(runToken);
            },
            new BraidOptions { Iterations = 1, Seed = 12345 },
            runToken);

        await Task.Delay(TimeSpan.FromMilliseconds(40), TimeProvider.System, DefaultCancellationToken);
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
            // External cancellation wins over the worker failure observed after cancel.
        }
    }

    /// <summary>Verifies an unused schedule with no forked workers fails clearly.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncFailsWhenScheduleNoWorkers()
    {
        var exception = await Assertions.ExpectsAsync<BraidRunException>(
            BraidRunner.RunAsync(
                static _ => Task.CompletedTask,
                new BraidOptions
                {
                    Iterations = 1,
                    Seed = 12345,
                    Schedule = BraidSchedule.Replay(new BraidStep("worker-1", "ready")),
                },
                DefaultCancellationToken));

        Assert.Contains("unused steps", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies forked startup workers are stopped when the callback throws before join.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncStopsStartupOnThrowAfterFork()
    {
        var runTask = BraidRunner.RunAsync(
            static context =>
            {
                context.Fork(static async () => await BraidProbe.HitAsync("ready", DefaultCancellationToken));

                context.Fork(static async () => await BraidProbe.HitAsync("ready", DefaultCancellationToken));

                context.Fork(static async () => await BraidProbe.HitAsync("ready", DefaultCancellationToken));

                throw new InvalidOperationException("callback failed before join");
            },
            new BraidOptions { Iterations = 1, Seed = 12345 },
            DefaultCancellationToken);

        var watchdog = Task.Delay(TimeSpan.FromSeconds(2), TimeProvider.System, DefaultCancellationToken);
        if (await Task.WhenAny(runTask, watchdog) != runTask)
            Assert.Fail("Run should not hang after callback throws.");

        try
        {
            await runTask;
            Assert.Fail("Expected BraidRunException.");
        }
        catch (BraidRunException ex)
        {
            var report = ex.ToString();
            Assert.Contains("callback failed before join", report, StringComparison.Ordinal);
            Assert.Contains("worker-1 forked", report, StringComparison.Ordinal);
            Assert.Contains("Trace:", report, StringComparison.Ordinal);
        }
    }

    /// <summary>Verifies timeout reports include worker and probe trace context.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task TimeoutReportIncludesRunningWorkerTrace()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            var runTask = BraidRunner.RunAsync(
                async context =>
                {
                    context.Fork(async () =>
                    {
                        await BraidProbe.HitAsync("started", DefaultCancellationToken);
                        await gate.Task.WaitAsync(DefaultCancellationToken);
                    });

                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions { Iterations = 1, Seed = 12345, Timeout = TimeSpan.FromMilliseconds(50) },
                DefaultCancellationToken);

            var watchdog = Task.Delay(TimeSpan.FromSeconds(2), TimeProvider.System, DefaultCancellationToken);
            if (await Task.WhenAny(runTask, watchdog) != runTask)
                Assert.Fail("Braid run did not complete before watchdog timeout.");

            try
            {
                await runTask;
                Assert.Fail("Expected BraidRunException.");
            }
            catch (BraidRunException exception)
            {
                var report = exception.ToString();
                Assert.Contains("braid run timed out.", report, StringComparison.Ordinal);
                Assert.Contains("started", report, StringComparison.Ordinal);
                Assert.Contains("worker-1", report, StringComparison.Ordinal);
                Assert.Contains("released", report, StringComparison.Ordinal);
            }
        }
        finally
        {
            _ = gate.TrySetResult();
        }
    }

    /// <summary>Verifies timeout wins when the worker failure happens only after the timeout window.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task TimeoutWinsOverLateWorkerFailure()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            var runTask = BraidRunner.RunAsync(
                async context =>
                {
                    context.Fork(async () =>
                    {
                        await BraidProbe.HitAsync("block", DefaultCancellationToken);
                        await gate.Task.WaitAsync(DefaultCancellationToken);
                        throw new InvalidOperationException("too late after timeout");
                    });

                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions { Iterations = 1, Seed = 12345, Timeout = TimeSpan.FromMilliseconds(50) },
                DefaultCancellationToken);

            var watchdog = Task.Delay(TimeSpan.FromSeconds(2), TimeProvider.System, DefaultCancellationToken);
            if (await Task.WhenAny(runTask, watchdog) != runTask)
                Assert.Fail("Braid run did not complete before watchdog timeout.");

            try
            {
                await runTask;
                Assert.Fail("Expected BraidRunException.");
            }
            catch (BraidRunException exception)
            {
                Assert.Contains("braid run timed out.", exception.Message, StringComparison.Ordinal);
                Assert.DoesNotContain("too late after timeout", exception.ToString(), StringComparison.Ordinal);
            }
        }
        finally
        {
            _ = gate.TrySetResult();
        }
    }

    /// <summary>Verifies worker failure is reported when it occurs before the iteration timeout.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task WorkerFailureWinsOverTimeout()
    {
        var exception = await Assertions.ExpectsAsync<BraidRunException>(
            BraidRunner.RunAsync(
                static async context =>
                {
                    context.Fork(static async () =>
                    {
                        await BraidProbe.HitAsync("before-failure", DefaultCancellationToken);
                        throw new InvalidOperationException("worker failed before timeout");
                    });

                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions { Iterations = 1, Seed = 12345, Timeout = TimeSpan.FromSeconds(5) },
                DefaultCancellationToken));

        var report = exception.ToString();
        Assert.Contains("worker failed before timeout", report, StringComparison.Ordinal);
        Assert.DoesNotContain("braid run timed out.", report, StringComparison.Ordinal);
    }
}
