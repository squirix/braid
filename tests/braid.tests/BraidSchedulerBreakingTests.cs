using Xunit;

namespace Braid.Tests;

/// <summary>
/// Deterministic stress tests for scheduler edge cases and invalid usage patterns.
/// </summary>
public sealed class BraidSchedulerBreakingTests : TestBase
{
    /// <summary>
    /// Verifies user JoinAsync plus outer RunAsync join does not double-release workers or throw semaphore errors.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncDoesNotReleaseWorkersTwiceWhenUserAlreadyJoined()
    {
        var runTask = Braid.RunAsync(
            static async context =>
            {
                context.Fork(static async () =>
                {
                    await BraidProbe.HitAsync("ready", DefaultCancellationToken);
                });

                await context.JoinAsync(DefaultCancellationToken);
            },
            new BraidOptions { Iterations = 1, Seed = 12345 },
            DefaultCancellationToken);

        await AssertRunCompletesBeforeWatchdogAsync(
            runTask,
            "Double join should complete without SemaphoreFullException or BraidRunException.");
    }

    /// <summary>
    /// Verifies a second JoinAsync after the first completed join is idempotent for a simple completed run.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task JoinAsyncCanBeCalledTwiceAfterCompletionOrFailsClearly()
    {
        var runTask = Braid.RunAsync(
            static async context =>
            {
                context.Fork(static async () =>
                {
                    await BraidProbe.HitAsync("ready", DefaultCancellationToken);
                });

                await context.JoinAsync(DefaultCancellationToken);
                await context.JoinAsync(DefaultCancellationToken);
            },
            new BraidOptions { Iterations = 1, Seed = 12345 },
            DefaultCancellationToken);

        await AssertRunCompletesBeforeWatchdogAsync(
            runTask,
            "Sequential second JoinAsync should not deadlock or throw SemaphoreFullException.");
    }

    /// <summary>
    /// Verifies a worker failure before any probe is surfaced on join with trace context.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncReportsWorkerFailureBeforeFirstProbe()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await Braid.RunAsync(
                static async context =>
                {
                    context.Fork(static () => Task.FromException(new InvalidOperationException("before-probe failure")));

                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions { Iterations = 1, Seed = 12345 },
                DefaultCancellationToken);
        });

        var report = exception.ToString();
        Assert.Contains("before-probe failure", report, StringComparison.Ordinal);
        Assert.Contains("worker-1", report, StringComparison.Ordinal);
        Assert.Contains("Trace:", report, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies HitAsync from the run callback without a current worker completes immediately (no current task).
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ProbeHitInsideRunButOutsideWorkerCompletesImmediately()
    {
        var runTask = Braid.RunAsync(
            static async context =>
            {
                await BraidProbe.HitAsync("callback-probe", DefaultCancellationToken);

                context.Fork(static async () =>
                {
                    await BraidProbe.HitAsync("worker-probe", DefaultCancellationToken);
                });

                await context.JoinAsync(DefaultCancellationToken);
            },
            new BraidOptions { Iterations = 1, Seed = 12345 },
            DefaultCancellationToken);

        await AssertRunCompletesBeforeWatchdogAsync(
            runTask,
            "Probe outside a forked worker should not deadlock.");
    }

    /// <summary>
    /// Verifies duplicate scripted steps for the same worker and probe are rejected or fail clearly after the worker completes.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task DuplicateScriptedReleaseForSameProbeFailsClearly()
    {
        var options = new BraidOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = BraidSchedule.Replay(
                new BraidStep("worker-1", "ready"),
                new BraidStep("worker-1", "ready")),
        };

        var exception = await Assert.ThrowsAsync<BraidRunException>(async () =>
        {
            await Braid.RunAsync(
                static async context =>
                {
                    context.Fork(static async () =>
                    {
                        await BraidProbe.HitAsync("ready", DefaultCancellationToken);
                    });

                    await context.JoinAsync(DefaultCancellationToken);
                },
                options,
                DefaultCancellationToken);
        });

        Assert.Contains("Scripted schedule contained unused steps after all workers completed.", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a scripted schedule with steps that no worker can satisfy after the run completes is reported as a failure.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncFailsWhenScriptedScheduleHasUnusedSteps()
    {
        var options = new BraidOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = BraidSchedule.Replay(
                new BraidStep("worker-1", "ready"),
                new BraidStep("worker-2", "never")),
        };

        var exception = await Assert.ThrowsAsync<BraidRunException>(async () =>
        {
            await Braid.RunAsync(
                static async context =>
                {
                    context.Fork(static async () =>
                    {
                        await BraidProbe.HitAsync("ready", DefaultCancellationToken);
                    });

                    await context.JoinAsync(DefaultCancellationToken);
                },
                options,
                DefaultCancellationToken);
        });

        Assert.Contains("Scripted schedule contained unused steps after all workers completed.", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies timeout surfaces as BraidRunException and the run does not hang when StopAsync waits on a non-cooperative worker.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncTimeoutDoesNotHangWhenRunningWorkerIgnoresCancellation()
    {
        var unblock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var runTask = Braid.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    await BraidProbe.HitAsync("at-probe", DefaultCancellationToken);
                    await unblock.Task;
                });

                await context.JoinAsync(DefaultCancellationToken);
            },
            new BraidOptions { Iterations = 1, Seed = 12345, Timeout = TimeSpan.FromMilliseconds(50) },
            DefaultCancellationToken);

        var watchdog = Task.Delay(TimeSpan.FromSeconds(2), DefaultCancellationToken);
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

    /// <summary>
    /// Verifies two concurrent JoinAsync calls from the same callback either both complete or fail clearly.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ConcurrentJoinAsyncCallsDoNotCorruptScheduler()
    {
        var runTask = Braid.RunAsync(
            static async context =>
            {
                context.Fork(static async () =>
                {
                    await BraidProbe.HitAsync("ready", DefaultCancellationToken);
                });

                context.Fork(static async () =>
                {
                    await BraidProbe.HitAsync("ready", DefaultCancellationToken);
                });

                var join1 = context.JoinAsync(DefaultCancellationToken);
                var join2 = context.JoinAsync(DefaultCancellationToken);
                await Task.WhenAll(join1, join2);
            },
            new BraidOptions { Iterations = 1, Seed = 12345 },
            DefaultCancellationToken);

        await AssertRunCompletesBeforeWatchdogAsync(
            runTask,
            "Concurrent JoinAsync should not deadlock or surface SemaphoreFullException.");
    }

    private static async Task AssertRunCompletesBeforeWatchdogAsync(Task runTask, string failureMessage)
    {
        var watchdog = Task.Delay(TimeSpan.FromSeconds(2), DefaultCancellationToken);
        var winner = await Task.WhenAny(runTask, watchdog);

        if (winner != runTask)
        {
            Assert.Fail($"Braid run did not complete before watchdog timeout. {failureMessage}");
        }

        await runTask;
    }
}
