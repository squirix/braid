using Xunit;

namespace Braid.Tests;

/// <summary>Covers isolation of worker failures so they are reported rather than masked by siblings.</summary>
public sealed class BraidWorkerFailureIsolationTests : TestBase
{
    /// <summary>Verifies multiple worker failures report one failure while trace still records both workers.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task MultipleWorkersOneFailureBothTraced()
    {
        var exception = await Assertions.ExpectsAsync<RunException>(
            Runner.RunAsync(
                static async context =>
                {
                    context.Fork(static async () =>
                    {
                        await Probe.HitAsync("first", DefaultCancellationToken);
                        throw new InvalidOperationException("worker one failed");
                    });

                    context.Fork(static async () =>
                    {
                        await Probe.HitAsync("second", DefaultCancellationToken);
                        throw new InvalidOperationException("worker two failed");
                    });

                    await context.JoinAsync(DefaultCancellationToken);
                },
                new RunOptions
                {
                    Iterations = 1,
                    Seed = 5103,
                    Schedule = ReplaySchedule.Replay(new ReplayStep("worker-1", "first"), new ReplayStep("worker-2", "second")),
                },
                DefaultCancellationToken));

        var report = exception.ToString();
        Assert.True(
            report.Contains("worker one failed", StringComparison.Ordinal) || report.Contains("worker two failed", StringComparison.Ordinal),
            "Expected at least one worker failure message in report.");
        Assert.Contains("worker-1", report, StringComparison.Ordinal);
        Assert.Contains("worker-2", report, StringComparison.Ordinal);
    }

    /// <summary>Verifies synchronously completing worker trace includes fork/startup release/complete.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task SynchronouslyCompletingWorkerFullTrace()
    {
        var exception = await Assertions.ExpectsAsync<RunException>(
            Runner.RunAsync(
                static async context =>
                {
                    context.Fork(static () => Task.CompletedTask);
                    await context.JoinAsync(DefaultCancellationToken);
                    throw new InvalidOperationException("fail-after-join");
                },
                new RunOptions { Iterations = 1, Seed = 5106 },
                DefaultCancellationToken));

        var report = exception.ToString();
        Assert.Contains("worker-1 forked", report, StringComparison.Ordinal);
        Assert.Contains("worker-1 released", report, StringComparison.Ordinal);
        Assert.Contains("worker-1 completed", report, StringComparison.Ordinal);
        Assert.DoesNotContain("released at", report, StringComparison.Ordinal);
    }

    /// <summary>Verifies synchronously throwing fork delegates are reported clearly.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task SynchronouslyThrowingWorkerReported()
    {
        var exception = await Assertions.ExpectsAsync<RunException>(
            Runner.RunAsync(
                static async context =>
                {
                    context.Fork(static () => throw new InvalidOperationException("sync throw"));
                    await context.JoinAsync(DefaultCancellationToken);
                },
                new RunOptions { Iterations = 1, Seed = 5107 },
                DefaultCancellationToken));

        var report = exception.ToString();
        Assert.Contains("sync throw", report, StringComparison.Ordinal);
        Assert.Contains("worker-1 forked", report, StringComparison.Ordinal);
        Assert.Contains("worker-1 completed", report, StringComparison.Ordinal);
    }

    /// <summary>Verifies primary worker failures are not masked by non-cooperative sibling workers.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task WorkerFailureNotMaskedDuringStop()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var exceptionTask = Assertions.ExpectsAsync<RunException>(
                Runner.RunAsync(
                    async context =>
                    {
                        context.Fork(static () => Task.FromException(new InvalidOperationException("primary worker failure")));
                        context.Fork(async () =>
                        {
                            await Probe.HitAsync("waiter", DefaultCancellationToken);
                            await gate.Task.WaitAsync(DefaultCancellationToken);
                        });

                        await context.JoinAsync(DefaultCancellationToken);
                    },
                    new RunOptions { Iterations = 1, Seed = 5102 },
                    DefaultCancellationToken));

            await AssertCompletesBeforeWatchdogAsync(exceptionTask, "Worker failure should not be masked by stop path.", TimeSpan.FromSeconds(3), false);
            var exception = await exceptionTask;
            Assert.Contains("primary worker failure", exception.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            _ = gate.TrySetResult();
        }
    }

    /// <summary>Verifies worker failure while sibling waits at probe stops sibling cleanly.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task WorkerFailureStopsWaitingSiblingCleanly()
    {
        var exceptionTask = Assertions.ExpectsAsync<RunException>(
            Runner.RunAsync(
                static async context =>
                {
                    context.Fork(static async () =>
                    {
                        await Probe.HitAsync("fail-ready", DefaultCancellationToken);
                        throw new InvalidOperationException("failing worker");
                    });

                    context.Fork(static async () => await Probe.HitAsync("blocked", DefaultCancellationToken));

                    await context.JoinAsync(DefaultCancellationToken);
                },
                new RunOptions
                {
                    Iterations = 1,
                    Seed = 5104,
                    Schedule = ReplaySchedule.Replay(new ReplayStep("worker-1", "fail-ready"), new ReplayStep("worker-2", "blocked")),
                },
                DefaultCancellationToken));

        await AssertCompletesBeforeWatchdogAsync(exceptionTask, "Run should fail without deadlock.", TimeSpan.FromSeconds(3), false);
        var exception = await exceptionTask;
        var report = exception.ToString();
        Assert.Contains("failing worker", report, StringComparison.Ordinal);
        Assert.Contains("worker-2 hit blocked", report, StringComparison.Ordinal);
    }
}
