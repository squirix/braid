using Xunit;

namespace Braid.Tests;

/// <summary>Covers schedulervalidation behavior of the braid scheduler and run reporting.</summary>
public sealed class BraidSchedulerValidationTests : TestBase
{
    /// <summary>Verifies shared default options are not mutated by runs.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task DefaultOptionsAreNotMutatedByRunAsync()
    {
        var beforeIterations = BraidOptions.Default.Iterations;
        var beforeSeed = BraidOptions.Default.Seed;
        var beforeTimeout = BraidOptions.Default.Timeout;
        var beforeSchedule = BraidOptions.Default.Schedule;

        await BraidRunner.RunAsync(static _ => Task.CompletedTask, DefaultCancellationToken);

        Assert.Equal(beforeIterations, BraidOptions.Default.Iterations);
        Assert.Equal(beforeSeed, BraidOptions.Default.Seed);
        Assert.Equal(beforeTimeout, BraidOptions.Default.Timeout);
        Assert.Same(beforeSchedule, BraidOptions.Default.Schedule);
    }

    /// <summary>Verifies duplicate scripted steps for the same worker and probe are rejected or fail clearly after the worker completes.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task DuplicateScriptedReleaseFailsClearly()
    {
        var options = new BraidOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = BraidSchedule.Replay(new BraidStep("worker-1", "ready"), new BraidStep("worker-1", "ready")),
        };

        var exception = await Assert.ThrowsAsync<BraidRunException>(async () =>
        {
            await BraidRunner.RunAsync(
                static async context =>
                {
                    context.Fork(static async () => await BraidProbe.HitAsync("ready", DefaultCancellationToken));

                    await context.JoinAsync(DefaultCancellationToken);
                },
                options,
                DefaultCancellationToken);
        });

        Assert.Contains("Scripted schedule contained unused steps after all workers completed.", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Verifies fork delegates that return null fail clearly.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ForkOperationReturningNullFailsClearly()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await BraidRunner.RunAsync(
                static async context =>
                {
                    context.Fork(NullTestValues.NullReturningFork);
                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions { Iterations = 1, Seed = 12345 },
                DefaultCancellationToken);
        });

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
            static () => BraidRunner.RunAsync(
                static async context =>
                {
                    context.Fork(static async () =>
                    {
                        _ = await Assert.ThrowsAnyAsync<ArgumentException>(static async () => await BraidProbe.HitAsync(NullTestValues.String, DefaultCancellationToken));
                        _ = await Assert.ThrowsAnyAsync<ArgumentException>(static async () => await BraidProbe.HitAsync(string.Empty, DefaultCancellationToken));
                        _ = await Assert.ThrowsAnyAsync<ArgumentException>(static async () => await BraidProbe.HitAsync(" ", DefaultCancellationToken));
                        await BraidProbe.HitAsync("ok", DefaultCancellationToken);
                    });

                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions { Iterations = 1, Seed = 12345 },
                DefaultCancellationToken),
            "Invalid probe names inside worker should throw ArgumentException without corrupting the run.");
    }

    /// <summary>Verifies invalid probe names are rejected outside a braid run.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task HitAsyncRejectsInvalidProbeOutsideRun()
    {
        _ = await Assert.ThrowsAnyAsync<ArgumentException>(static async () => await BraidProbe.HitAsync(NullTestValues.String, DefaultCancellationToken));
        _ = await Assert.ThrowsAnyAsync<ArgumentException>(static async () => await BraidProbe.HitAsync(string.Empty, DefaultCancellationToken));
        _ = await Assert.ThrowsAnyAsync<ArgumentException>(static async () => await BraidProbe.HitAsync(" ", DefaultCancellationToken));
    }

    /// <summary>Verifies callback null-task failures are clearly reported.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncCallbackReturnsNullFailsClearly()
    {
        var exception =
            await Assert.ThrowsAsync<BraidRunException>(static async () => await BraidRunner.RunAsync(NullTestValues.NullReturningRunCallback, DefaultCancellationToken));

        var report = exception.ToString();
        Assert.DoesNotContain(nameof(NullReferenceException), report, StringComparison.Ordinal);
        Assert.Contains("null", report, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("callback", report, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies a scripted schedule with steps that no worker can satisfy after the run completes is reported as a failure.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncFailsWhenScheduleHasUnusedSteps()
    {
        var options = new BraidOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = BraidSchedule.Replay(new BraidStep("worker-1", "ready"), new BraidStep("worker-2", "never")),
        };

        var exception = await Assert.ThrowsAsync<BraidRunException>(async () =>
        {
            await BraidRunner.RunAsync(
                static async context =>
                {
                    context.Fork(static async () => await BraidProbe.HitAsync("ready", DefaultCancellationToken));

                    await context.JoinAsync(DefaultCancellationToken);
                },
                options,
                DefaultCancellationToken);
        });

        Assert.Contains("Scripted schedule contained unused steps after all workers completed.", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Verifies empty runs fail with non-empty replay schedules.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncFailsNoWorkersNonEmptySchedule()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await BraidRunner.RunAsync(
                static _ => Task.CompletedTask,
                new BraidOptions
                {
                    Iterations = 1,
                    Seed = 25,
                    Schedule = BraidSchedule.Replay(new BraidStep("worker-1", "ready")),
                },
                DefaultCancellationToken);
        });

        var report = exception.ToString();
        Assert.Contains("unused steps", report, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Schedule:", report, StringComparison.Ordinal);
    }

    /// <summary>Verifies empty runs complete with empty replay schedules.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncCompletesNoWorkersEmptySchedule()
    {
        var options = new BraidOptions { Iterations = 1, Seed = 24, Schedule = BraidSchedule.Replay() };
        await BraidRunner.RunAsync(static _ => Task.CompletedTask, options, DefaultCancellationToken);
        Assert.Empty(options.Schedule.Steps);
    }

    /// <summary>Verifies probe-free workers complete with empty replay schedules.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task WorkerNoProbesCompletesEmptySchedule()
    {
        var options = new BraidOptions { Iterations = 1, Seed = 23, Schedule = BraidSchedule.Replay() };
        await BraidRunner.RunAsync(
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
        await BraidRunner.RunAsync(
            context =>
            {
                context.Fork(() =>
                {
                    _ = Interlocked.Increment(ref counter);
                    return Task.CompletedTask;
                });

                return context.JoinAsync(DefaultCancellationToken);
            },
            new BraidOptions { Iterations = 1, Seed = 21 },
            DefaultCancellationToken);

        Assert.Equal(1, counter);
    }

    /// <summary>Verifies probe-free workers fail when replay steps are configured.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task WorkerNoProbesFailsWhenProbeSteps()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await BraidRunner.RunAsync(
                static async context =>
                {
                    context.Fork(static () => Task.CompletedTask);
                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions
                {
                    Iterations = 1,
                    Seed = 22,
                    Schedule = BraidSchedule.Replay(new BraidStep("worker-1", "ready")),
                },
                DefaultCancellationToken);
        });

        var report = exception.ToString();
        Assert.Contains("unused steps", report, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("worker-1 completed", report, StringComparison.Ordinal);
    }
}
