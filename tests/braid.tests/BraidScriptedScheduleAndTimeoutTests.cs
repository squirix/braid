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

    /// <summary>Verifies a scripted schedule replays independently for each iteration.</summary>
    /// <param name="iterations">The number of iterations the scripted schedule is replayed.</param>
    /// <param name="seed">The deterministic seed for the run.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Theory]
    [InlineData(2, 5403)]
    [InlineData(3, 5402)]
    public async Task ScriptedScheduleReplaysForEachIteration(int iterations, int seed)
    {
        var completed = new CompletionCounter();
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
            new BraidOptions
            {
                Iterations = iterations,
                Seed = seed,
                Schedule = BraidSchedule.Replay(new BraidStep("worker-1", "ready")),
            },
            DefaultCancellationToken);

        Assert.Equal(iterations, completed.Value);
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
            var exceptionTask = Assertions.ExpectsAsync<BraidRunException>(
                BraidRunner.RunAsync(
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
                    DefaultCancellationToken));

            await AssertCompletesBeforeWatchdogAsync(exceptionTask, "Timeout run should fail deterministically.", TimeSpan.FromSeconds(3), false);
            var exception = await exceptionTask;
            Assert.Contains("braid run timed out.", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            _ = gate.TrySetResult();
        }

        await AssertCompletesBeforeWatchdogAsync(workerFinallyObserved.Task, "Worker finally should complete after timeout.", TimeSpan.FromSeconds(3), false);
        await BraidProbe.HitAsync("outside-after-timeout", DefaultCancellationToken);
    }

    private static async Task RunCanceledProbeLeakCheckAsync(int runIndex)
    {
        _ = await Assertions.ExpectsAsync<BraidRunException>(
            BraidRunner.RunAsync(
                static async context =>
                {
                    context.Fork(static () => BraidProbe.HitAsync("ready", new CancellationToken(true)).AsTask());
                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions { Iterations = 1, Seed = 5200 + runIndex },
                DefaultCancellationToken));

        await BraidProbe.HitAsync($"outside-canceled-{runIndex}", DefaultCancellationToken);
    }
}
