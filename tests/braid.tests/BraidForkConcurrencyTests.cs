using System.Collections.Concurrent;
using TUnit.Assertions.Enums;

namespace Braid.Tests;

/// <summary>Covers fork concurrency, racing with join, and many-worker scheduling determinism.</summary>
public sealed class BraidForkConcurrencyTests : TestBase
{
    /// <summary>Verifies concurrent fork calls before join assign unique workers and complete.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ConcurrentForkBeforeJoinUniqueWorkerIds(CancellationToken cancellationToken)
    {
        var completed = new CompletionCounter();

        var operation = Runner.RunAsync(
            async context =>
            {
                var forks = new Task[20];
                for (var forkIndex = 0; forkIndex < forks.Length; forkIndex++)
                    forks[forkIndex] = ScheduleConcurrentForkAsync(context, completed, cancellationToken);

                await Task.WhenAll(forks);
                await context.JoinAsync(cancellationToken);
                throw new InvalidOperationException("forced-failure");
            },
            new RunOptions { Iterations = 1, Seed = 5502 },
            cancellationToken);
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(operation);

        _ = await Assert.That(completed.Value).IsEqualTo(20);

        var distinctWorkerIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in exception.Traces)
        {
            if (entry.EndsWith(" forked", StringComparison.Ordinal))
                _ = distinctWorkerIds.Add(entry);
        }

        _ = await Assert.That(distinctWorkerIds.Count).IsEqualTo(20);
    }

    /// <summary>Verifies forking from an external task during active join fails clearly.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ForkFromExternalTaskFailsClearly(CancellationToken cancellationToken)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await Runner.RunAsync(
                async context =>
                {
                    context.Fork(async () =>
                    {
                        await Probe.HitAsync("ready", cancellationToken);
                        await gate.Task.WaitAsync(cancellationToken);
                    });

                    var joinTask = context.JoinAsync(cancellationToken);
                    await Task.Yield();

                    Exception? forkException = null;
                    try
                    {
                        await StartNewOnThreadPoolAsync(() => context.Fork(static () => Task.CompletedTask), cancellationToken);
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or RunException)
                    {
                        forkException = ex;
                    }

                    _ = await Assert.That(forkException).IsNotNull();
                    _ = await Assert.That(forkException is InvalidOperationException or RunException).IsTrue().Because($"Unexpected fork exception type: {forkException.GetType().FullName}");

                    _ = gate.TrySetResult();
                    await joinTask;
                },
                new RunOptions { Iterations = 1, Seed = 5501, Timeout = TimeSpan.FromSeconds(2) },
                cancellationToken);
        }
        finally
        {
            _ = gate.TrySetResult();
        }
    }

    /// <summary>Verifies fork racing with join either succeeds consistently or fails clearly.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ForkRacingFailsClearlyOrCompletes(CancellationToken cancellationToken)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var completed = 0;
            await Runner.RunAsync(
                async context =>
                {
                    context.Fork(async () =>
                    {
                        await Probe.HitAsync("ready", cancellationToken);
                        _ = Interlocked.Increment(ref completed);
                        await gate.Task.WaitAsync(cancellationToken);
                    });

                    var joinTask = context.JoinAsync(cancellationToken);
                    Exception? forkException = null;
                    try
                    {
                        await StartNewOnThreadPoolAsync(
                            () =>
                            {
                                context.Fork(() =>
                                {
                                    _ = Interlocked.Increment(ref completed);
                                    return Task.CompletedTask;
                                });
                            },
                            cancellationToken);
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or RunException)
                    {
                        forkException = ex;
                    }

                    _ = gate.TrySetResult();
                    await joinTask;

                    if (forkException == null)
                    {
                        _ = await Assert.That(completed == 1 || completed == 2).IsTrue().Because($"Expected completed workers to be 1 or 2 but observed {completed}.");
                        return;
                    }

                    _ = await Assert.That(forkException is InvalidOperationException or RunException).IsTrue().Because($"Unexpected fork exception type: {forkException.GetType().FullName}");
                },
                new RunOptions { Iterations = 1, Seed = 5503, Timeout = TimeSpan.FromSeconds(2) },
                cancellationToken);
        }
        finally
        {
            _ = gate.TrySetResult();
        }
    }

    /// <summary>Verifies many probe-free workers complete successfully.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ManyProbeFreeWorkersComplete(CancellationToken cancellationToken)
    {
        var completed = new CompletionCounter();
        await Runner.RunAsync(
            async context =>
            {
                for (var workerIndex = 0; workerIndex < 200; workerIndex++)
                    ForkIncrementCompleted(context, completed);

                await context.JoinAsync(cancellationToken);
            },
            new RunOptions { Iterations = 1, Seed = 5302, Timeout = TimeSpan.FromSeconds(2) },
            cancellationToken);

        _ = await Assert.That(completed.Value).IsEqualTo(200);
    }

    /// <summary>Verifies many synchronously failing workers do not hang join.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ManySynchronouslyFailingNoHangJoin(CancellationToken cancellationToken)
    {
        var operation = Runner.RunAsync(
            async context =>
            {
                for (var workerIndex = 0; workerIndex < 20; workerIndex++)
                    ForkSyncFailWorker(context, workerIndex);

                await context.JoinAsync(cancellationToken);
            },
            new RunOptions { Iterations = 1, Seed = 5301 },
            cancellationToken);
        var exceptionTask = BraidAssertions.AssertExpectsAsync<RunException>(operation);

        await AssertCompletesBeforeWatchdogAsync(exceptionTask, "Join should fail quickly for many synchronous failures.", TimeSpan.FromSeconds(3), false, cancellationToken);
        var exception = await exceptionTask;
        var report = exception.ToString();
        _ = await Assert.That(report).Contains("sync-fail-");
        var forkedTraceCount = 0;
        for (var traceIndex = 0; traceIndex < exception.Traces.Count; traceIndex++)
        {
            if (exception.Traces[traceIndex].Contains("forked", StringComparison.Ordinal))
                forkedTraceCount++;
        }

        _ = await Assert.That(forkedTraceCount >= 20).IsTrue();
    }

    /// <summary>Verifies many workers at same probe can follow scripted reverse order.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ManyWorkersSameProbeOrderCompletes(CancellationToken cancellationToken)
    {
        const int workerCount = 20;
        var releaseOrder = new ConcurrentQueue<string>();
        var steps = new ReplayStep[workerCount];
        var stepIndex = 0;
        for (var workerIndex = workerCount; workerIndex >= 1; workerIndex--)
            steps[stepIndex++] = new ReplayStep($"worker-{workerIndex}", "ready");

        await Runner.RunAsync(
            async context =>
            {
                for (var workerIndex = 1; workerIndex <= workerCount; workerIndex++)
                    ForkHitReadyForWorker(context, workerIndex, releaseOrder, cancellationToken);

                await context.JoinAsync(cancellationToken);
            },
            new RunOptions
            {
                Iterations = 1,
                Seed = 5303,
                Schedule = ReplaySchedule.Replay(steps),
            },
            cancellationToken);

        var expectedOrder = new List<string>(workerCount);
        for (var workerIndex = workerCount; workerIndex >= 1; workerIndex--)
            expectedOrder.Add($"worker-{workerIndex}");

        _ = await Assert.That(releaseOrder).IsEquivalentTo(expectedOrder, CollectionOrdering.Matching);
    }

    /// <summary>Verifies random scheduling with same seed remains stable under parallel background noise.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task SameSeedSchedulingStableUnderNoise(CancellationToken cancellationToken)
    {
        var first = await RunScenarioAsync(cancellationToken);
        var second = await RunScenarioAsync(cancellationToken);
        _ = await Assert.That(second).IsEqualTo(first);
        return;

        static async Task<string> RunScenarioAsync(CancellationToken cancellationToken)
        {
            using var noiseCts = new CancellationTokenSource();
            var noiseToken = noiseCts.Token;

            var noiseTasks = new Task[4];
            for (var noiseIndex = 0; noiseIndex < noiseTasks.Length; noiseIndex++)
                noiseTasks[noiseIndex] = StartNoiseYieldLoopAsync(noiseToken, cancellationToken);

            try
            {
                var operation = Runner.RunAsync(
                    async context =>
                    {
                        context.Fork(async () =>
                        {
                            await Probe.HitAsync("a", cancellationToken);
                            await Probe.HitAsync("a2", cancellationToken);
                        });

                        context.Fork(async () =>
                        {
                            await Probe.HitAsync("b", cancellationToken);
                            await Probe.HitAsync("b2", cancellationToken);
                        });

                        await context.JoinAsync(cancellationToken);
                        throw new InvalidOperationException("forced-failure");
                    },
                    new RunOptions { Iterations = 1, Seed = 5401 },
                    cancellationToken);
                var exception = await BraidAssertions.AssertExpectsAsync<RunException>(operation);

                return exception.ToString().ReplaceLineEndings("\n");
            }
            finally
            {
                await noiseCts.CancelAsync();
                await Task.WhenAll(noiseTasks);
            }
        }
    }
}
