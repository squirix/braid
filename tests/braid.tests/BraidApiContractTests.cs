namespace Braid.Tests;

/// <summary>Covers the public braid API contract.</summary>
public sealed class BraidApiContractTests : TestBase
{
    /// <summary>Verifies fork after join starts fails with a braid run exception.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public Task ForkAfterJoinStartedFailsClearly(CancellationToken cancellationToken)
    {
        return Runner.RunAsync(
            async context =>
            {
                await context.JoinAsync(cancellationToken);

                var exception = BraidAssertions.AssertExpects<RunException, RunContext>(context, static state => state.Fork(static () => Task.CompletedTask));
                _ = await Assert.That(exception.Message).Contains("Cannot fork after JoinAsync has started.");
            },
            new RunOptions { Iterations = 1, Seed = 12345 },
            cancellationToken);
    }

    /// <summary>Verifies fork validation rejects a null operation.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public Task ForkThrowsForNullOperation(CancellationToken cancellationToken)
    {
        return Runner.RunAsync(
            static context =>
            {
                _ = BraidAssertions.AssertExpects<ArgumentNullException, RunContext>(context, static state => state.Fork(NullTestValues.ForkOperation));
                return Task.CompletedTask;
            },
            new RunOptions { Iterations = 1, Seed = 12345 },
            cancellationToken);
    }

    /// <summary>Verifies fork validation rejects a null worker id.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public Task ForkWithWorkerIdThrowsForNullWorkerId(CancellationToken cancellationToken)
    {
        return Runner.RunAsync(
            static context =>
            {
                _ = BraidAssertions.AssertExpects<ArgumentNullException, RunContext>(context, static state => state.Fork(NullTestValues.String, static () => Task.CompletedTask));
                return Task.CompletedTask;
            },
            new RunOptions { Iterations = 1, Seed = 12345 },
            cancellationToken);
    }

    /// <summary>Verifies a named fork uses the supplied worker id in the scheduling trace.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ForkWorkerIdUsesStableWorkerIdInTrace(CancellationToken cancellationToken)
    {
        RunContext? capturedContext = null;

        await Runner.RunAsync(
            async context =>
            {
                capturedContext = context;
                context.Fork("reader", async () => await Probe.HitAsync("ready", cancellationToken));
                await context.JoinAsync(cancellationToken);
            },
            new RunOptions { Iterations = 1, Seed = 12345 },
            cancellationToken);

        _ = await Assert.That(capturedContext).IsNotNull();
        _ = await Assert.That(capturedContext.TraceSteps).Contains("reader forked");
        _ = await Assert.That(capturedContext.TraceSteps).Contains("reader hit ready");
    }

    /// <summary>Verifies probe validation rejects invalid names.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task HitAsyncRejectsInvalidProbeNames(CancellationToken cancellationToken)
    {
        _ = await BraidAssertions.AssertExpectsAnyAsync<ArgumentException, (string? Name, CancellationToken Token)>(
            (NullTestValues.String, cancellationToken),
            static state => Probe.HitAsync(state.Name!, state.Token));
        _ = await BraidAssertions.AssertExpectsAnyAsync<ArgumentException, (string? Name, CancellationToken Token)>(
            (string.Empty, cancellationToken),
            static state => Probe.HitAsync(state.Name!, state.Token));
        _ = await BraidAssertions.AssertExpectsAnyAsync<ArgumentException, (string? Name, CancellationToken Token)>(
            (" ", cancellationToken),
            static state => Probe.HitAsync(state.Name!, state.Token));
    }

    /// <summary>Verifies replay schedules snapshot the supplied steps.</summary>
    [Test]
    public async Task ReplaySnapshotsInputArray()
    {
        var steps = new[] { new ReplayStep("worker-1", "ready") };

        var schedule = ReplaySchedule.Replay(steps);
        steps[0] = new ReplayStep("worker-2", "changed");

        _ = await Assert.That(schedule.Steps[0]).IsEqualTo(new ReplayStep("worker-1", "ready"));
    }

    /// <summary>Verifies replay validation rejects a null steps array.</summary>
    [Test]
    public void ReplayThrowsForNullStepsArray() => _ = BraidAssertions.AssertExpects<ArgumentNullException>(static () => _ = ReplaySchedule.Replay(NullTestValues.ReplaySteps));

    /// <summary>Verifies null options use default options.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task RunAsyncAcceptsNullOptions(CancellationToken cancellationToken)
    {
        var ran = false;

        await Runner.RunAsync(
            context =>
            {
                _ = context;
                ran = true;
                return Task.CompletedTask;
            },
            null,
            cancellationToken);

        _ = await Assert.That(ran).IsTrue();
    }

    /// <summary>Verifies invalid timeouts are rejected before the run starts.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    [Test]
    public async Task RunAsyncRejectsInvalidTimeoutAtStart(CancellationToken cancellationToken)
    {
        var ran = new bool[1];

        _ = BraidAssertions.AssertExpects<ArgumentOutOfRangeException, CancellationToken, bool[]>(
            cancellationToken,
            ran,
            static (token, flag) =>
            {
                _ = Runner.RunAsync(
                    context =>
                    {
                        _ = context;
                        flag[0] = true;
                        return Task.CompletedTask;
                    },
                    new RunOptions { Timeout = TimeSpan.Zero },
                    token);
            });

        _ = await Assert.That(ran[0]).IsFalse();
    }

    /// <summary>Verifies run validation rejects a null test delegate.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    [Test]
    public void RunAsyncThrowsForNullTestDelegate(CancellationToken cancellationToken) =>
        _ = BraidAssertions.AssertExpects<ArgumentNullException, CancellationToken>(cancellationToken, static token => _ = Runner.RunAsync(NullTestValues.RunCallback, token));

    /// <summary>Verifies a null schedule is exposed as an empty schedule.</summary>
    [Test]
    public async Task RunExceptionExposesNullScheduleAsEmpty()
    {
        var exception = new RunException("failed", 12345, 0, ["trace"], null, null);

        _ = await Assert.That(exception.Steps).IsEmpty();
    }

    /// <summary>Verifies braid run exceptions snapshot trace and schedule values.</summary>
    [Test]
    public async Task RunExceptionSnapshotsTraceAndSchedule()
    {
        var trace = new[] { "worker-1 forked" };
        var schedule = new[] { new ReplayStep("worker-1", "ready") };

        var exception = new RunException("failed", 12345, 0, trace, schedule, null);
        trace[0] = "changed";
        schedule[0] = new ReplayStep("worker-2", "changed");

        _ = await Assert.That(exception.Traces).IsEquivalentTo(["worker-1 forked"]);
        _ = await Assert.That(exception.Steps).IsEquivalentTo([new ReplayStep("worker-1", "ready")]);
        _ = await Assert.That(exception.SchedulerDiagnostics).IsNull();
    }

    /// <summary>Verifies invalid iteration counts are rejected before the run starts.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    [Test]
    public async Task RunRejectsInvalidIterationsBeforeStart(CancellationToken cancellationToken)
    {
        var ran = new bool[1];

        _ = BraidAssertions.AssertExpects<ArgumentOutOfRangeException, CancellationToken, bool[]>(
            cancellationToken,
            ran,
            static (token, flag) =>
            {
                _ = Runner.RunAsync(
                    context =>
                    {
                        _ = context;
                        flag[0] = true;
                        return Task.CompletedTask;
                    },
                    new RunOptions { Iterations = 0 },
                    token);
            });

        _ = await Assert.That(ran[0]).IsFalse();
    }
}
