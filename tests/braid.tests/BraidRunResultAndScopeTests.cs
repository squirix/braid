using Xunit;

namespace Braid.Tests;

/// <summary>Covers run-result snapshots, public-surface immutability, and run-scope cleanup.</summary>
public sealed class BraidRunResultAndScopeTests : TestBase
{
    /// <summary>Verifies failure reports snapshot schedule and are not affected by later caller mutations.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunExceptionSnapshotsTraceAndSchedule()
    {
        var backing = new[] { new BraidStep("worker-1", "ready") };
        var schedule = BraidSchedule.Replay(backing);

        var exception = await Assert.ThrowsAsync<BraidRunException>(async () =>
        {
            await BraidRunner.RunAsync(
                static async context =>
                {
                    context.Fork(static async () =>
                    {
                        await BraidProbe.HitAsync("ready", DefaultCancellationToken);
                        throw new InvalidOperationException("after-ready");
                    });

                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions { Iterations = 1, Seed = 12345, Schedule = schedule },
                DefaultCancellationToken);
        });

        backing[0] = new BraidStep("worker-9", "mutated");

        var report = exception.ToString();
        Assert.Contains("worker-1 @ ready", report, StringComparison.Ordinal);
        Assert.DoesNotContain("worker-9", report, StringComparison.Ordinal);
        Assert.Equal(new BraidStep("worker-1", "ready"), exception.Schedule[0]);
    }

    /// <summary>
    /// Verifies <see cref="BraidRunException.ToString" /> does not mutate between calls.
    /// </summary>
    [Fact]
    public void BraidRunExceptionToStringIsStable()
    {
        var exception = new BraidRunException("failed", 42, 3, ["worker-1 forked"], [new BraidStep("worker-1", "ready")], new InvalidOperationException("inner"));

        var first = exception.ToString();
        var second = exception.ToString();

        Assert.Equal(first, second);
    }

    /// <summary>
    /// Verifies schedule steps exposed from <see cref="BraidSchedule" /> cannot be mutated as a list.
    /// </summary>
    [Fact]
    public void ScheduleStepsCannotBeMutatedPublic()
    {
        var schedule = BraidSchedule.Replay(new BraidStep("worker-1", "ready"));
        var steps = schedule.Steps;

        Assert.Equal(new BraidStep("worker-1", "ready"), steps[0]);

        var list = Assert.IsAssignableFrom<IList<BraidStep>>(steps);
        _ = Assert.Throws<NotSupportedException>(() => list.Add(new BraidStep("worker-2", "x")));
        _ = Assert.Throws<NotSupportedException>(list.Clear);
        _ = Assert.Throws<NotSupportedException>(() => list[0] = new BraidStep("worker-9", "mutated"));

        Assert.Equal(new BraidStep("worker-1", "ready"), schedule.Steps[0]);
    }

    /// <summary>Verifies AsyncLocal scope is cleared after a successful run.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncClearsRunScopeAfterSuccessfulRun()
    {
        await BraidRunner.RunAsync(
            static async context =>
            {
                context.Fork(static async () => await BraidProbe.HitAsync("ready", DefaultCancellationToken));

                await context.JoinAsync(DefaultCancellationToken);
            },
            new BraidOptions { Iterations = 1, Seed = 12345 },
            DefaultCancellationToken);

        var probe = BraidProbe.HitAsync("outside-run", DefaultCancellationToken);
        Assert.True(probe.IsCompletedSuccessfully);
        await probe;
    }

    /// <summary>Verifies AsyncLocal scope is cleared after a timeout failure.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncClearsRunScopeAfterTimeout()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var runTask = BraidRunner.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    await BraidProbe.HitAsync("block", DefaultCancellationToken);
                    await gate.Task.WaitAsync(DefaultCancellationToken);
                });

                await context.JoinAsync(DefaultCancellationToken);
            },
            new BraidOptions { Iterations = 1, Seed = 12345, Timeout = TimeSpan.FromMilliseconds(50) },
            DefaultCancellationToken);

        var watchdog = Task.Delay(TimeSpan.FromSeconds(2), TimeProvider.System, DefaultCancellationToken);
        if (await Task.WhenAny(runTask, watchdog) != runTask)
        {
            _ = gate.TrySetResult();
            Assert.Fail("Braid run did not complete before watchdog timeout.");
        }

        _ = gate.TrySetResult();

        try
        {
            await runTask;
            Assert.Fail("Expected BraidRunException.");
        }
        catch (BraidRunException exception)
        {
            Assert.Contains("braid run timed out.", exception.Message, StringComparison.Ordinal);
        }

        var probe = BraidProbe.HitAsync("outside-run", DefaultCancellationToken);
        Assert.True(probe.IsCompletedSuccessfully);
        await probe;
    }

    /// <summary>Verifies a run with no workers and no schedule completes.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncCompletesWithNoWorkersSchedule()
    {
        var options = new BraidOptions { Iterations = 1, Seed = 12345 };
        await BraidRunner.RunAsync(static _ => Task.CompletedTask, options, DefaultCancellationToken);

        var probe = BraidProbe.HitAsync("outside-run", DefaultCancellationToken);
        Assert.True(probe.IsCompletedSuccessfully);
        await probe;
    }

    /// <summary>Verifies cancellation is observed before the user callback runs.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncSurfacesCancellationBeforeFork()
    {
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        var executed = false;

        _ = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await BraidRunner.RunAsync(
                context =>
                {
                    executed = true;
                    _ = context;
                    return Task.CompletedTask;
                },
                new BraidOptions { Iterations = 1, Seed = 12345 },
                canceled.Token);
        });

        Assert.False(executed);
    }
}
