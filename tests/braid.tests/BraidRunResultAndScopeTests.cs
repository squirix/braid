namespace Braid.Tests;

/// <summary>Covers run-result snapshots, public-surface immutability, and run-scope cleanup.</summary>
public sealed class BraidRunResultAndScopeTests : TestBase
{
    /// <summary>Verifies <see cref="RunException.ToString" /> does not mutate between calls.</summary>
    [Test]
    public async Task BraidRunExceptionToStringIsStable()
    {
        var exception = new RunException("failed", 42, 3, ["worker-1 forked"], [new ReplayStep("worker-1", "ready")], new InvalidOperationException("inner"));

        var first = exception.ToString();
        var second = exception.ToString();

        _ = await Assert.That(second).IsEqualTo(first);
    }

    /// <summary>Verifies AsyncLocal scope is cleared after a successful run.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task RunAsyncClearsRunScopeAfterSuccessfulRun(CancellationToken cancellationToken)
    {
        await Runner.RunAsync(
            async context =>
            {
                context.Fork(async () => await Probe.HitAsync("ready", cancellationToken));

                await context.JoinAsync(cancellationToken);
            },
            new RunOptions { Iterations = 1, Seed = 12345 },
            cancellationToken);

        await AssertProbeIsNoOpOutsideRunAsync(cancellationToken);
    }

    /// <summary>Verifies AsyncLocal scope is cleared after a timeout failure.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task RunAsyncClearsRunScopeAfterTimeout(CancellationToken cancellationToken)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var runTask = Runner.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    await Probe.HitAsync("block", cancellationToken);
                    await gate.Task.WaitAsync(cancellationToken);
                });

                await context.JoinAsync(cancellationToken);
            },
            new RunOptions { Iterations = 1, Seed = 12345, Timeout = TimeSpan.FromMilliseconds(50) },
            cancellationToken);

        var watchdog = Task.Delay(TimeSpan.FromSeconds(2), TimeProvider.System, cancellationToken);
        if (await Task.WhenAny(runTask, watchdog) != runTask)
        {
            _ = gate.TrySetResult();
            Assert.Fail("Braid run did not complete before watchdog timeout.");
        }

        _ = gate.TrySetResult();

        try
        {
            await runTask;
            Assert.Fail("Expected RunException.");
        }
        catch (RunException exception)
        {
            _ = await Assert.That(exception.Message).Contains("braid run timed out.");
        }

        await AssertProbeIsNoOpOutsideRunAsync(cancellationToken);
    }

    /// <summary>Verifies a run with no workers and no schedule completes.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task RunAsyncCompletesWithNoWorkersSchedule(CancellationToken cancellationToken)
    {
        var options = new RunOptions { Iterations = 1, Seed = 12345 };
        await Runner.RunAsync(static _ => Task.CompletedTask, options, cancellationToken);

        await AssertProbeIsNoOpOutsideRunAsync(cancellationToken);
    }

    /// <summary>Verifies cancellation is observed before the user callback runs.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task RunAsyncSurfacesCancellationBeforeFork()
    {
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        var executed = new[] { false };

        _ = BraidAssertions.AssertExpects<OperationCanceledException, CancellationTokenSource, bool[]>(
            canceled,
            executed,
            static (state, executed) =>
            {
                _ = Runner.RunAsync(
                    context =>
                    {
                        executed[0] = true;
                        _ = context;
                        return Task.CompletedTask;
                    },
                    new RunOptions { Iterations = 1, Seed = 12345 },
                    state.Token);
            });

        _ = await Assert.That(executed[0]).IsFalse();
    }

    /// <summary>Verifies failure reports snapshot schedule and are not affected by later caller mutations.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task RunExceptionSnapshotsTraceAndSchedule(CancellationToken cancellationToken)
    {
        var backing = new[] { new ReplayStep("worker-1", "ready") };
        var schedule = ReplaySchedule.Replay(backing);

        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                async context =>
                {
                    context.Fork(async () =>
                    {
                        await Probe.HitAsync("ready", cancellationToken);
                        throw new InvalidOperationException("after-ready");
                    });

                    await context.JoinAsync(cancellationToken);
                },
                new RunOptions { Iterations = 1, Seed = 12345, Schedule = schedule },
                cancellationToken));

        backing[0] = new ReplayStep("worker-9", "mutated");

        var report = exception.ToString();
        _ = await Assert.That(report).Contains("worker-1 @ ready");
        _ = await Assert.That(report).DoesNotContain("worker-9");
        _ = await Assert.That(exception.Steps[0]).IsEqualTo(new ReplayStep("worker-1", "ready"));
    }

    /// <summary>Verifies schedule steps exposed from <see cref="ReplaySchedule" /> cannot be mutated as a list.</summary>
    [Test]
    public async Task ScheduleStepsCannotBeMutatedPublic()
    {
        var schedule = ReplaySchedule.Replay(new ReplayStep("worker-1", "ready"));
        var steps = schedule.Steps;

        _ = await Assert.That(steps[0]).IsEqualTo(new ReplayStep("worker-1", "ready"));

        var list = await Assert.That(steps).IsTypeOf<IList<ReplayStep>>();
        _ = BraidAssertions.AssertExpects<NotSupportedException, IList<ReplayStep>>(list!, static state => state.Add(new ReplayStep("worker-2", "x")));
        _ = BraidAssertions.AssertExpects<NotSupportedException, IList<ReplayStep>>(list!, static state => state.Clear());
        _ = BraidAssertions.AssertExpects<NotSupportedException, IList<ReplayStep>>(list!, static state => state[0] = new ReplayStep("worker-9", "mutated"));

        _ = await Assert.That(schedule.Steps[0]).IsEqualTo(new ReplayStep("worker-1", "ready"));
    }
}
