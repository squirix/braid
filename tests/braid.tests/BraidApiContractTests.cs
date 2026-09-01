using Xunit;

namespace Braid.Tests;

/// <summary>Covers the public braid API contract.</summary>
public sealed class BraidApiContractTests : TestBase
{
    /// <summary>Verifies fork after join starts fails with a braid run exception.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public Task ForkAfterJoinStartedFailsClearly()
    {
        return Runner.RunAsync(
            static async context =>
            {
                await context.JoinAsync(DefaultCancellationToken);

                var exception = Assertions.Expects<RunException, RunContext>(context, static state => state.Fork(static () => Task.CompletedTask));
                Assert.Contains("Cannot fork after JoinAsync has started.", exception.Message, StringComparison.Ordinal);
            },
            new RunOptions { Iterations = 1, Seed = 12345 },
            DefaultCancellationToken);
    }

    /// <summary>Verifies fork validation rejects a null operation.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public Task ForkThrowsForNullOperation()
    {
        return Runner.RunAsync(
            static context =>
            {
                _ = Assertions.Expects<ArgumentNullException, RunContext>(context, static state => state.Fork(NullTestValues.ForkOperation));
                return Task.CompletedTask;
            },
            new RunOptions { Iterations = 1, Seed = 12345 },
            DefaultCancellationToken);
    }

    /// <summary>Verifies fork validation rejects a null worker id.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public Task ForkWithWorkerIdThrowsForNullWorkerId()
    {
        return Runner.RunAsync(
            static context =>
            {
                _ = Assertions.Expects<ArgumentNullException, RunContext>(context, static state => state.Fork(NullTestValues.String, static () => Task.CompletedTask));
                return Task.CompletedTask;
            },
            new RunOptions { Iterations = 1, Seed = 12345 },
            DefaultCancellationToken);
    }

    /// <summary>Verifies a named fork uses the supplied worker id in the scheduling trace.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ForkWorkerIdUsesStableWorkerIdInTrace()
    {
        RunContext? capturedContext = null;

        await Runner.RunAsync(
            async context =>
            {
                capturedContext = context;
                context.Fork("reader", static async () => await Probe.HitAsync("ready", DefaultCancellationToken));
                await context.JoinAsync(DefaultCancellationToken);
            },
            new RunOptions { Iterations = 1, Seed = 12345 },
            DefaultCancellationToken);

        Assert.NotNull(capturedContext);
        Assert.Contains("reader forked", capturedContext.TraceSteps, StringComparer.Ordinal);
        Assert.Contains("reader hit ready", capturedContext.TraceSteps, StringComparer.Ordinal);
    }

    /// <summary>Verifies probe validation rejects invalid names.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task HitAsyncRejectsInvalidProbeNames()
    {
        _ = await Assertions.ExpectsAnyAsync<ArgumentException>(static () => Probe.HitAsync(NullTestValues.String, DefaultCancellationToken));
        _ = await Assertions.ExpectsAnyAsync<ArgumentException>(static () => Probe.HitAsync(string.Empty, DefaultCancellationToken));
        _ = await Assertions.ExpectsAnyAsync<ArgumentException>(static () => Probe.HitAsync(" ", DefaultCancellationToken));
    }

    /// <summary>Verifies replay schedules snapshot the supplied steps.</summary>
    [Fact]
    public void ReplaySnapshotsInputArray()
    {
        var steps = new[] { new ReplayStep("worker-1", "ready") };

        var schedule = ReplaySchedule.Replay(steps);
        steps[0] = new ReplayStep("worker-2", "changed");

        Assert.Equal(new ReplayStep("worker-1", "ready"), schedule.Steps[0]);
    }

    /// <summary>Verifies replay validation rejects a null steps array.</summary>
    [Fact]
    public void ReplayThrowsForNullStepsArray() => _ = Assertions.Expects<ArgumentNullException>(static () => _ = ReplaySchedule.Replay(NullTestValues.ReplaySteps));

    /// <summary>Verifies null options use default options.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncAcceptsNullOptions()
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
            DefaultCancellationToken);

        Assert.True(ran);
    }

    /// <summary>Verifies invalid timeouts are rejected before the run starts.</summary>
    [Fact]
    public void RunAsyncRejectsInvalidTimeoutAtStart()
    {
        var ran = false;

        _ = Assertions.Expects<ArgumentOutOfRangeException>(() =>
        {
            _ = Runner.RunAsync(
                context =>
                {
                    _ = context;
                    ran = true;
                    return Task.CompletedTask;
                },
                new RunOptions { Timeout = TimeSpan.Zero },
                DefaultCancellationToken);
        });

        Assert.False(ran);
    }

    /// <summary>Verifies run validation rejects a null test delegate.</summary>
    [Fact]
    public void RunAsyncThrowsForNullTestDelegate() =>
        _ = Assertions.Expects<ArgumentNullException>(static () => _ = Runner.RunAsync(NullTestValues.RunCallback, DefaultCancellationToken));

    /// <summary>Verifies a null schedule is exposed as an empty schedule.</summary>
    [Fact]
    public void RunExceptionExposesNullScheduleAsEmpty()
    {
        var exception = new RunException("failed", 12345, 0, ["trace"], null, null);

        Assert.Empty(exception.Steps);
    }

    /// <summary>Verifies braid run exceptions snapshot trace and schedule values.</summary>
    [Fact]
    public void RunExceptionSnapshotsTraceAndSchedule()
    {
        var trace = new[] { "worker-1 forked" };
        var schedule = new[] { new ReplayStep("worker-1", "ready") };

        var exception = new RunException("failed", 12345, 0, trace, schedule, null);
        trace[0] = "changed";
        schedule[0] = new ReplayStep("worker-2", "changed");

        Assert.Equal(["worker-1 forked"], exception.Traces);
        Assert.Equal([new ReplayStep("worker-1", "ready")], exception.Steps);
        Assert.Null(exception.SchedulerDiagnostics);
    }

    /// <summary>Verifies invalid iteration counts are rejected before the run starts.</summary>
    [Fact]
    public void RunRejectsInvalidIterationsBeforeStart()
    {
        var ran = false;

        _ = Assertions.Expects<ArgumentOutOfRangeException>(() =>
        {
            _ = Runner.RunAsync(
                context =>
                {
                    _ = context;
                    ran = true;
                    return Task.CompletedTask;
                },
                new RunOptions { Iterations = 0 },
                DefaultCancellationToken);
        });

        Assert.False(ran);
    }
}
