using Xunit;

namespace Braid.Tests;

/// <summary>Covers scripted schedule replay behavior and timeout/cleanup guarantees.</summary>
public sealed class BraidScriptedScheduleAndTimeoutTests : TestBase
{
    /// <summary>Verifies repeated canceled worker-local probes do not leak scope or hang.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ManyCanceledWorkerProbesNoLeakHang()
    {
        for (var runIndex = 0; runIndex < 100; runIndex++)
            await RunCanceledProbeLeakCheckAsync(runIndex);
    }

    /// <summary>Verifies scripted scheduler waits for running worker to satisfy expected step.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ScheduleWaitsForWorkerExpectedProbe()
    {
        var probesHit = new CompletionCounter();
        var options = new BraidOptions
        {
            Iterations = 1,
            Seed = 5105,
            Schedule = BraidSchedule.Replay(new BraidStep("worker-1", "later"), new BraidStep("worker-2", "other")),
        };

        await BraidRunner.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    await Task.Yield();
                    await BraidProbe.HitAsync("later", DefaultCancellationToken);
                    _ = probesHit.Increment();
                });

                context.Fork(async () =>
                {
                    await BraidProbe.HitAsync("other", DefaultCancellationToken);
                    _ = probesHit.Increment();
                });

                await context.JoinAsync(DefaultCancellationToken);
            },
            options,
            DefaultCancellationToken);

        Assert.Equal(2, probesHit.Value);
        await BraidProbe.HitAsync("outside-schedule-wait", DefaultCancellationToken);
    }

    /// <summary>Verifies schedule cursor resets for each iteration.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ScriptedScheduleCursorResetsIteration()
    {
        var completed = new CompletionCounter();
        var options = new BraidOptions
        {
            Iterations = 2,
            Seed = 5403,
            Schedule = BraidSchedule.Replay(new BraidStep("worker-1", "ready")),
        };

        await BraidRunner.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    await BraidProbe.HitAsync("ready", DefaultCancellationToken);
                    _ = completed.Increment();
                });
                await context.JoinAsync(DefaultCancellationToken);
            },
            options,
            DefaultCancellationToken);

        Assert.Equal(2, completed.Value);
    }

    /// <summary>Verifies scripted schedules replay independently for each iteration.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ScriptedScheduleReplaysForEachIteration()
    {
        var completed = 0;
        await BraidRunner.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    await BraidProbe.HitAsync("ready", DefaultCancellationToken);
                    _ = Interlocked.Increment(ref completed);
                });
                await context.JoinAsync(DefaultCancellationToken);
            },
            new BraidOptions
            {
                Iterations = 3,
                Seed = 5402,
                Schedule = BraidSchedule.Replay(new BraidStep("worker-1", "ready")),
            },
            DefaultCancellationToken);

        Assert.Equal(3, completed);
    }

    /// <summary>Verifies worker finally after timeout does not change the surfaced timeout failure.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task WorkerFinallyTimeoutNoObjectException()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var workerFinallyObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            var exceptionTask = Assert.ThrowsAsync<BraidRunException>(async () =>
            {
                await BraidRunner.RunAsync(
                    async context =>
                    {
                        context.Fork(async () =>
                        {
                            try
                            {
                                await BraidProbe.HitAsync("ready", DefaultCancellationToken);
                                await gate.Task.WaitAsync(DefaultCancellationToken);
                            }
                            finally
                            {
                                _ = workerFinallyObserved.TrySetResult();
                            }
                        });

                        await context.JoinAsync(DefaultCancellationToken);
                    },
                    new BraidOptions { Iterations = 1, Seed = 5504, Timeout = TimeSpan.FromMilliseconds(50) },
                    DefaultCancellationToken);
            });

            AssertCompletesBeforeWatchdog(exceptionTask, "Timeout run should fail deterministically.", TimeSpan.FromSeconds(3), false);
            var exception = await exceptionTask;
            Assert.Contains("braid run timed out.", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            _ = gate.TrySetResult();
        }

        AssertCompletesBeforeWatchdog(workerFinallyObserved.Task, "Worker finally should complete after timeout.", TimeSpan.FromSeconds(3), false);
        await BraidProbe.HitAsync("outside-after-timeout", DefaultCancellationToken);
    }

    private static async Task RunCanceledProbeLeakCheckAsync(int runIndex)
    {
        _ = await Assert.ThrowsAsync<BraidRunException>(async () =>
        {
            await BraidRunner.RunAsync(
                static async context =>
                {
                    context.Fork(static () => BraidProbe.HitAsync("ready", new CancellationToken(true)).AsTask());
                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions { Iterations = 1, Seed = 5200 + runIndex },
                DefaultCancellationToken);
        });

        await BraidProbe.HitAsync($"outside-canceled-{runIndex}", DefaultCancellationToken);
    }
}
