namespace Braid.Tests;

/// <summary>Covers scripted schedule replay behavior and timeout/cleanup guarantees.</summary>
public sealed class BraidScriptedScheduleAndTimeoutTests : TestBase
{
    /// <summary>Verifies repeated canceled worker-local probes do not leak scope or hang.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ManyCanceledWorkerProbesNoLeakHang(CancellationToken cancellationToken)
    {
        for (var runIndex = 0; runIndex < 100; runIndex++)
            await RunCanceledProbeLeakCheckAsync(runIndex, cancellationToken);
    }

    /// <summary>Verifies scripted scheduler waits for running worker to satisfy expected step.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ScheduleWaitsForWorkerExpectedProbe(CancellationToken cancellationToken)
    {
        var probesHit = new CompletionCounter();
        var options = new RunOptions
        {
            Iterations = 1,
            Seed = 5105,
            Schedule = ReplaySchedule.Replay(new ReplayStep("worker-1", "later"), new ReplayStep("worker-2", "other")),
        };

        await Runner.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    await Task.Yield();
                    await Probe.HitAsync("later", cancellationToken);
                    _ = probesHit.Increment();
                });

                context.Fork(async () =>
                {
                    await Probe.HitAsync("other", cancellationToken);
                    _ = probesHit.Increment();
                });

                await context.JoinAsync(cancellationToken);
            },
            options,
            cancellationToken);

        _ = await Assert.That(probesHit.Value).IsEqualTo(2);
        await Probe.HitAsync("outside-schedule-wait", cancellationToken);
    }

    /// <summary>Verifies a scripted schedule replays independently for each iteration.</summary>
    /// <param name="iterations">The number of iterations the scripted schedule is replayed.</param>
    /// <param name="seed">The deterministic seed for the run.</param>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    [Arguments(2, 5403)]
    [Arguments(3, 5402)]
    public async Task ScriptedScheduleReplaysForEachIteration(int iterations, int seed, CancellationToken cancellationToken)
    {
        var completed = new CompletionCounter();
        await Runner.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    await Probe.HitAsync("ready", cancellationToken);
                    _ = completed.Increment();
                });
                await context.JoinAsync(cancellationToken);
            },
            new RunOptions
            {
                Iterations = iterations,
                Seed = seed,
                Schedule = ReplaySchedule.Replay(new ReplayStep("worker-1", "ready")),
            },
            cancellationToken);

        _ = await Assert.That(completed.Value).IsEqualTo(iterations);
    }

    /// <summary>Verifies worker finally after timeout does not change the surfaced timeout failure.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task WorkerFinallyTimeoutNoObjectException(CancellationToken cancellationToken)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var workerFinallyObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            var exceptionTask = BraidAssertions.AssertExpectsAsync<RunException>(
                Runner.RunAsync(
                    async context =>
                    {
                        context.Fork(async () =>
                        {
                            try
                            {
                                await Probe.HitAsync("ready", cancellationToken);
                                await gate.Task.WaitAsync(cancellationToken);
                            }
                            finally
                            {
                                _ = workerFinallyObserved.TrySetResult();
                            }
                        });

                        await context.JoinAsync(cancellationToken);
                    },
                    new RunOptions { Iterations = 1, Seed = 5504, Timeout = TimeSpan.FromMilliseconds(50) },
                    cancellationToken));

            await AssertCompletesBeforeWatchdogAsync(exceptionTask, "Timeout run should fail deterministically.", TimeSpan.FromSeconds(3), false, cancellationToken);
            var exception = await exceptionTask;
            _ = await Assert.That(exception.Message).Contains("braid run timed out.");
        }
        finally
        {
            _ = gate.TrySetResult();
        }

        await AssertCompletesBeforeWatchdogAsync(workerFinallyObserved.Task, "Worker finally should complete after timeout.", TimeSpan.FromSeconds(3), false, cancellationToken);
        await Probe.HitAsync("outside-after-timeout", cancellationToken);
    }

    private static async Task RunCanceledProbeLeakCheckAsync(int runIndex, CancellationToken cancellationToken = default)
    {
        _ = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                async context =>
                {
                    context.Fork(static () => Probe.HitAsync("ready", new CancellationToken(true)).AsTask());
                    await context.JoinAsync(cancellationToken);
                },
                new RunOptions { Iterations = 1, Seed = 5200 + runIndex },
                cancellationToken));

        await Probe.HitAsync($"outside-canceled-{runIndex}", cancellationToken);
    }
}
