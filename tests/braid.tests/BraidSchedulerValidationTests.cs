using Xunit;

namespace Braid.Tests;

/// <summary>Covers scheduler validation behavior of the braid scheduler and run reporting.</summary>
public sealed class BraidSchedulerValidationTests : TestBase
{
    /// <summary>Verifies shared default options are not mutated by runs.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task DefaultOptionsAreNotMutatedByRunAsync()
    {
        var beforeIterations = RunOptions.Default.Iterations;
        var beforeSeed = RunOptions.Default.Seed;
        var beforeTimeout = RunOptions.Default.Timeout;
        var beforeSchedule = RunOptions.Default.Schedule;

        await Runner.RunAsync(static _ => Task.CompletedTask, DefaultCancellationToken);

        Assert.Equal(beforeIterations, RunOptions.Default.Iterations);
        Assert.Equal(beforeSeed, RunOptions.Default.Seed);
        Assert.Equal(beforeTimeout, RunOptions.Default.Timeout);
        Assert.Same(beforeSchedule, RunOptions.Default.Schedule);
    }

    /// <summary>Verifies duplicate scripted steps for the same worker and probe are rejected or fail clearly after the worker completes.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task DuplicateScriptedHitStepFailsClearly()
    {
        var options = new RunOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = ReplaySchedule.Replay(new ReplayStep("worker-1", "ready"), new ReplayStep("worker-1", "ready")),
        };

        var operation = Runner.RunAsync(
            static async context =>
            {
                context.Fork(static async () => await Probe.HitAsync("ready", DefaultCancellationToken));

                await context.JoinAsync(DefaultCancellationToken);
            },
            options,
            DefaultCancellationToken);

        var exception = await Assertions.ExpectsAsync<RunException>(operation);

        Assert.Contains("Scripted schedule contained unused steps after all workers completed.", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Verifies fork delegates that return null fail clearly.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ForkOperationReturningNullFailsClearly()
    {
        var operation = Runner.RunAsync(
            static async context =>
            {
                context.Fork(NullTestValues.NullReturningFork);
                await context.JoinAsync(DefaultCancellationToken);
            },
            new RunOptions { Iterations = 1, Seed = 12345 },
            DefaultCancellationToken);

        var exception = await Assertions.ExpectsAsync<RunException>(operation);

        var report = exception.ToString();
        Assert.Contains("A forked operation failed.", exception.Message, StringComparison.Ordinal);
        Assert.True(
            report.Contains("null", StringComparison.OrdinalIgnoreCase) || report.Contains("Fork operation", StringComparison.OrdinalIgnoreCase),
            $"Expected clear null-task messaging. Report:{Environment.NewLine}{report}");
    }

    /// <summary>Verifies invalid probe names are rejected inside a worker before scheduler state is corrupted.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public Task HitAsyncRejectsInvalidProbeInsideWorker()
    {
        return AssertCompletesBeforeWatchdogAsync(
            static () => Runner.RunAsync(
                static async context =>
                {
                    context.Fork(static async () =>
                    {
                        _ = await Assertions.ExpectsAnyAsync<ArgumentException>(static () => Probe.HitAsync(NullTestValues.String, DefaultCancellationToken));
                        _ = await Assertions.ExpectsAnyAsync<ArgumentException>(static () => Probe.HitAsync(string.Empty, DefaultCancellationToken));
                        _ = await Assertions.ExpectsAnyAsync<ArgumentException>(static () => Probe.HitAsync(" ", DefaultCancellationToken));
                        await Probe.HitAsync("ok", DefaultCancellationToken);
                    });

                    await context.JoinAsync(DefaultCancellationToken);
                },
                new RunOptions { Iterations = 1, Seed = 12345 },
                DefaultCancellationToken),
            "Invalid probe names inside worker should throw ArgumentException without corrupting the run.");
    }

    /// <summary>Verifies invalid probe names are rejected outside a braid run.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task HitAsyncRejectsInvalidProbeOutsideRun()
    {
        _ = await Assertions.ExpectsAnyAsync<ArgumentException>(static () => Probe.HitAsync(NullTestValues.String, DefaultCancellationToken));
        _ = await Assertions.ExpectsAnyAsync<ArgumentException>(static () => Probe.HitAsync(string.Empty, DefaultCancellationToken));
        _ = await Assertions.ExpectsAnyAsync<ArgumentException>(static () => Probe.HitAsync(" ", DefaultCancellationToken));
    }

    /// <summary>Verifies callback null-task failures are clearly reported.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncCallbackReturnsNullFailsClearly()
    {
        var operation = Runner.RunAsync(NullTestValues.NullReturningRunCallback, DefaultCancellationToken);

        var exception = await Assertions.ExpectsAsync<RunException>(operation);

        var report = exception.ToString();
        Assert.DoesNotContain(nameof(NullReferenceException), report, StringComparison.Ordinal);
        Assert.Contains("null", report, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("callback", report, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies empty runs complete with empty replay schedules.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncCompletesNoWorkersEmptySchedule()
    {
        var options = new RunOptions { Iterations = 1, Seed = 24, Schedule = ReplaySchedule.Replay() };
        await Runner.RunAsync(static _ => Task.CompletedTask, options, DefaultCancellationToken);
        Assert.Empty(options.Schedule.Steps);
    }

    /// <summary>Verifies empty runs fail with non-empty replay schedules.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncFailsNoWorkersNonEmptySchedule()
    {
        var operation = Runner.RunAsync(
            static _ => Task.CompletedTask,
            new RunOptions
            {
                Iterations = 1,
                Seed = 25,
                Schedule = ReplaySchedule.Replay(new ReplayStep("worker-1", "ready")),
            },
            DefaultCancellationToken);

        var exception = await Assertions.ExpectsAsync<RunException>(operation);

        var report = exception.ToString();
        Assert.Contains("unused steps", report, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Schedule:", report, StringComparison.Ordinal);
    }

    /// <summary>Verifies a scripted schedule with steps that no worker can satisfy after the run completes is reported as a failure.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncFailsWhenScheduleHasUnusedSteps()
    {
        var options = new RunOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = ReplaySchedule.Replay(new ReplayStep("worker-1", "ready"), new ReplayStep("worker-2", "never")),
        };

        var operation = Runner.RunAsync(
            static async context =>
            {
                context.Fork(static async () => await Probe.HitAsync("ready", DefaultCancellationToken));

                await context.JoinAsync(DefaultCancellationToken);
            },
            options,
            DefaultCancellationToken);

        var exception = await Assertions.ExpectsAsync<RunException>(operation);

        Assert.Contains("Scripted schedule contained unused steps after all workers completed.", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Verifies probe-free workers complete with empty replay schedules.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task WorkerNoProbesCompletesEmptySchedule()
    {
        var options = new RunOptions { Iterations = 1, Seed = 23, Schedule = ReplaySchedule.Replay() };
        await Runner.RunAsync(
            static async context =>
            {
                context.Fork(static () => Task.CompletedTask);
                await context.JoinAsync(DefaultCancellationToken);
            },
            options,
            DefaultCancellationToken);

        Assert.Empty(options.Schedule.Steps);
    }

    /// <summary>Verifies probe-free workers can complete without schedules.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task WorkerNoProbesCompletesWithoutSchedule()
    {
        var counter = 0;
        await Runner.RunAsync(
            context =>
            {
                context.Fork(() =>
                {
                    _ = Interlocked.Increment(ref counter);
                    return Task.CompletedTask;
                });

                return context.JoinAsync(DefaultCancellationToken);
            },
            new RunOptions { Iterations = 1, Seed = 21 },
            DefaultCancellationToken);

        Assert.Equal(1, counter);
    }

    /// <summary>Verifies probe-free workers fail when replay steps are configured.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task WorkerNoProbesFailsWhenProbeSteps()
    {
        var operation = Runner.RunAsync(
            static async context =>
            {
                context.Fork(static () => Task.CompletedTask);
                await context.JoinAsync(DefaultCancellationToken);
            },
            new RunOptions
            {
                Iterations = 1,
                Seed = 22,
                Schedule = ReplaySchedule.Replay(new ReplayStep("worker-1", "ready")),
            },
            DefaultCancellationToken);

        var exception = await Assertions.ExpectsAsync<RunException>(operation);

        var report = exception.ToString();
        Assert.Contains("unused steps", report, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("worker-1 completed", report, StringComparison.Ordinal);
    }
}
