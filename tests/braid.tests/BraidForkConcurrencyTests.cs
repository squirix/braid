using System.Collections.Concurrent;
using Xunit;

namespace Braid.Tests;

/// <summary>Covers fork concurrency, racing with join, and many-worker scheduling determinism.</summary>
public sealed class BraidForkConcurrencyTests : TestBase
{
    /// <summary>Verifies concurrent fork calls before join assign unique workers and complete.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ConcurrentForkBeforeJoinUniqueWorkerIds()
    {
        var completed = new CompletionCounter();

        var exception = await Assert.ThrowsAsync<BraidRunException>(async () =>
        {
            await BraidRunner.RunAsync(
                async context =>
                {
                    var forks = new Task[20];
                    for (var forkIndex = 0; forkIndex < forks.Length; forkIndex++)
                        forks[forkIndex] = ScheduleConcurrentForkAsync(context, completed);

                    await Task.WhenAll(forks);
                    await context.JoinAsync(DefaultCancellationToken);
                    throw new InvalidOperationException("forced-failure");
                },
                new BraidOptions { Iterations = 1, Seed = 5502 },
                DefaultCancellationToken);
        });

        Assert.Equal(20, completed.Value);

        var distinctWorkerIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in exception.Trace)
            if (entry.EndsWith(" forked", StringComparison.Ordinal))
                _ = distinctWorkerIds.Add(entry);

        Assert.Equal(20, distinctWorkerIds.Count);
    }

    /// <summary>Verifies forking from an external task during active join fails clearly.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ForkFromExternalTaskFailsClearly()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await BraidRunner.RunAsync(
                async context =>
                {
                    context.Fork(async () =>
                    {
                        await BraidProbe.HitAsync("ready", DefaultCancellationToken);
                        await gate.Task.WaitAsync(DefaultCancellationToken);
                    });

                    var joinTask = context.JoinAsync(DefaultCancellationToken);
                    await Task.Yield();

                    var forkException = await Record.ExceptionAsync(() => StartNewOnThreadPoolAsync(() => context.Fork(static () => Task.CompletedTask), DefaultCancellationToken));

                    Assert.NotNull(forkException);
                    Assert.True(forkException is InvalidOperationException or BraidRunException, $"Unexpected fork exception type: {forkException.GetType().FullName}");

                    _ = gate.TrySetResult();
                    await joinTask;
                },
                new BraidOptions { Iterations = 1, Seed = 5501, Timeout = TimeSpan.FromSeconds(2) },
                DefaultCancellationToken);
        }
        finally
        {
            _ = gate.TrySetResult();
        }
    }

    /// <summary>Verifies fork racing with join either succeeds consistently or fails clearly.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ForkRacingFailsClearlyOrCompletes()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var completed = 0;
            await BraidRunner.RunAsync(
                async context =>
                {
                    context.Fork(async () =>
                    {
                        await BraidProbe.HitAsync("ready", DefaultCancellationToken);
                        _ = Interlocked.Increment(ref completed);
                        await gate.Task.WaitAsync(DefaultCancellationToken);
                    });

                    var joinTask = context.JoinAsync(DefaultCancellationToken);
                    var forkException = await Record.ExceptionAsync(() => StartNewOnThreadPoolAsync(
                        () =>
                        {
                            context.Fork(() =>
                            {
                                _ = Interlocked.Increment(ref completed);
                                return Task.CompletedTask;
                            });
                        },
                        DefaultCancellationToken));

                    _ = gate.TrySetResult();
                    await joinTask;

                    if (forkException == null)
                    {
                        Assert.True(completed is 1 or 2);
                        return;
                    }

                    Assert.True(forkException is InvalidOperationException or BraidRunException, $"Unexpected fork exception type: {forkException.GetType().FullName}");
                },
                new BraidOptions { Iterations = 1, Seed = 5503, Timeout = TimeSpan.FromSeconds(2) },
                DefaultCancellationToken);
        }
        finally
        {
            _ = gate.TrySetResult();
        }
    }

    /// <summary>Verifies many probe-free workers complete successfully.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ManyProbeFreeWorkersComplete()
    {
        var completed = new CompletionCounter();
        await BraidRunner.RunAsync(
            async context =>
            {
                for (var workerIndex = 0; workerIndex < 200; workerIndex++)
                    ForkIncrementCompleted(context, completed);

                await context.JoinAsync(DefaultCancellationToken);
            },
            new BraidOptions { Iterations = 1, Seed = 5302, Timeout = TimeSpan.FromSeconds(2) },
            DefaultCancellationToken);

        Assert.Equal(200, completed.Value);
    }

    /// <summary>Verifies many synchronously failing workers do not hang join.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ManySynchronouslyFailingNoHangJoin()
    {
        var exceptionTask = Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await BraidRunner.RunAsync(
                static async context =>
                {
                    for (var workerIndex = 0; workerIndex < 20; workerIndex++)
                        ForkSyncFailWorker(context, workerIndex);

                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions { Iterations = 1, Seed = 5301 },
                DefaultCancellationToken);
        });

        AssertCompletesBeforeWatchdog(exceptionTask, "Join should fail quickly for many synchronous failures.", TimeSpan.FromSeconds(3), false);
        var exception = await exceptionTask;
        var report = exception.ToString();
        Assert.Contains("sync-fail-", report, StringComparison.Ordinal);
        var forkedTraceCount = 0;
        for (var traceIndex = 0; traceIndex < exception.Trace.Count; traceIndex++)
            if (exception.Trace[traceIndex].Contains("forked", StringComparison.Ordinal))
                forkedTraceCount++;

        Assert.True(forkedTraceCount >= 20);
    }

    /// <summary>Verifies many workers at same probe can follow scripted reverse order.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ManyWorkersSameProbeOrderCompletes()
    {
        const int workerCount = 20;
        var releaseOrder = new ConcurrentQueue<string>();
        var steps = new BraidStep[workerCount];
        var stepIndex = 0;
        for (var workerIndex = workerCount; workerIndex >= 1; workerIndex--)
            steps[stepIndex++] = new BraidStep($"worker-{workerIndex}", "ready");

        await BraidRunner.RunAsync(
            async context =>
            {
                for (var workerIndex = 1; workerIndex <= workerCount; workerIndex++)
                    ForkHitReadyForWorker(context, workerIndex, releaseOrder);

                await context.JoinAsync(DefaultCancellationToken);
            },
            new BraidOptions
            {
                Iterations = 1,
                Seed = 5303,
                Schedule = BraidSchedule.Replay(steps),
            },
            DefaultCancellationToken);

        var expectedOrder = new List<string>(workerCount);
        for (var workerIndex = workerCount; workerIndex >= 1; workerIndex--)
            expectedOrder.Add($"worker-{workerIndex}");

        Assert.Equal(expectedOrder, releaseOrder, StringComparer.Ordinal);
    }

    /// <summary>Verifies random scheduling with same seed remains stable under parallel background noise.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task SameSeedSchedulingStableUnderNoise()
    {
        var first = await RunScenarioAsync();
        var second = await RunScenarioAsync();
        Assert.Equal(first, second);
        return;

        static async Task<string> RunScenarioAsync()
        {
            using var noiseCts = new CancellationTokenSource();
            var noiseToken = noiseCts.Token;

            var noiseTasks = new Task[4];
            for (var noiseIndex = 0; noiseIndex < noiseTasks.Length; noiseIndex++)
                noiseTasks[noiseIndex] = StartNoiseYieldLoopAsync(noiseToken);

            try
            {
                var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
                {
                    await BraidRunner.RunAsync(
                        static async context =>
                        {
                            context.Fork(static async () =>
                            {
                                await BraidProbe.HitAsync("a", DefaultCancellationToken);
                                await BraidProbe.HitAsync("a2", DefaultCancellationToken);
                            });

                            context.Fork(static async () =>
                            {
                                await BraidProbe.HitAsync("b", DefaultCancellationToken);
                                await BraidProbe.HitAsync("b2", DefaultCancellationToken);
                            });

                            await context.JoinAsync(DefaultCancellationToken);
                            throw new InvalidOperationException("forced-failure");
                        },
                        new BraidOptions { Iterations = 1, Seed = 5401 },
                        DefaultCancellationToken);
                });

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
