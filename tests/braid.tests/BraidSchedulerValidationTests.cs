namespace Braid.Tests;

/// <summary>Covers scheduler validation behavior of the braid scheduler and run reporting.</summary>
public sealed class BraidSchedulerValidationTests : TestBase
{
    /// <summary>Verifies shared default options are not mutated by runs.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task DefaultOptionsAreNotMutatedByRunAsync(CancellationToken cancellationToken)
    {
        var beforeIterations = RunOptions.Default.Iterations;
        var beforeSeed = RunOptions.Default.Seed;
        var beforeTimeout = RunOptions.Default.Timeout;
        var beforeSchedule = RunOptions.Default.Schedule;

        await Runner.RunAsync(static _ => Task.CompletedTask, cancellationToken);

        _ = await Assert.That(RunOptions.Default.Iterations).IsEqualTo(beforeIterations);
        _ = await Assert.That(RunOptions.Default.Seed).IsEqualTo(beforeSeed);
        _ = await Assert.That(RunOptions.Default.Timeout).IsEqualTo(beforeTimeout);
        _ = await Assert.That(RunOptions.Default.Schedule).IsSameReferenceAs(beforeSchedule);
    }

    /// <summary>Verifies duplicate scripted steps for the same worker and probe are rejected or fail clearly after the worker completes.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task DuplicateScriptedHitStepFailsClearly(CancellationToken cancellationToken)
    {
        var options = new RunOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = ReplaySchedule.Replay(new ReplayStep("worker-1", "ready"), new ReplayStep("worker-1", "ready")),
        };

        var operation = Runner.RunAsync(
            async context =>
            {
                context.Fork(async () => await Probe.HitAsync("ready", cancellationToken));

                await context.JoinAsync(cancellationToken);
            },
            options,
            cancellationToken);

        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(operation);

        _ = await Assert.That(exception.Message).Contains("Scripted schedule contained unused steps after all workers completed.");
    }

    /// <summary>Verifies fork delegates that return null fail clearly.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ForkOperationReturningNullFailsClearly(CancellationToken cancellationToken)
    {
        var operation = Runner.RunAsync(
            async context =>
            {
                context.Fork(NullTestValues.NullReturningFork);
                await context.JoinAsync(cancellationToken);
            },
            new RunOptions { Iterations = 1, Seed = 12345 },
            cancellationToken);

        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(operation);

        var report = exception.ToString();
        _ = await Assert.That(exception.Message).Contains("A forked operation failed.");
        _ = await Assert.That(report.Contains("null", StringComparison.OrdinalIgnoreCase) || report.Contains("Fork operation", StringComparison.OrdinalIgnoreCase)).IsTrue().Because($"Expected clear null-task messaging. Report:{Environment.NewLine}{report}");
    }

    /// <summary>Verifies invalid probe names are rejected inside a worker before scheduler state is corrupted.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public Task HitAsyncRejectsInvalidProbeInsideWorker(CancellationToken cancellationToken)
    {
        return AssertCompletesBeforeWatchdogAsync(
            () => Runner.RunAsync(
                async context =>
                {
                    context.Fork(async () =>
                    {
                        _ = await BraidAssertions.AssertExpectsAnyAsync<ArgumentException>(() => Probe.HitAsync(NullTestValues.String, cancellationToken));
                        _ = await BraidAssertions.AssertExpectsAnyAsync<ArgumentException>(() => Probe.HitAsync(string.Empty, cancellationToken));
                        _ = await BraidAssertions.AssertExpectsAnyAsync<ArgumentException>(() => Probe.HitAsync(" ", cancellationToken));
                        await Probe.HitAsync("ok", cancellationToken);
                    });

                    await context.JoinAsync(cancellationToken);
                },
                new RunOptions { Iterations = 1, Seed = 12345 },
                cancellationToken),
            "Invalid probe names inside worker should throw ArgumentException without corrupting the run.",
            cancellationToken: cancellationToken);
    }

    /// <summary>Verifies invalid probe names are rejected outside a braid run.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task HitAsyncRejectsInvalidProbeOutsideRun(CancellationToken cancellationToken)
    {
        _ = await BraidAssertions.AssertExpectsAnyAsync<ArgumentException>(() => Probe.HitAsync(NullTestValues.String, cancellationToken));
        _ = await BraidAssertions.AssertExpectsAnyAsync<ArgumentException>(() => Probe.HitAsync(string.Empty, cancellationToken));
        _ = await BraidAssertions.AssertExpectsAnyAsync<ArgumentException>(() => Probe.HitAsync(" ", cancellationToken));
    }

    /// <summary>Verifies callback null-task failures are clearly reported.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task RunAsyncCallbackReturnsNullFailsClearly(CancellationToken cancellationToken)
    {
        var operation = Runner.RunAsync(NullTestValues.NullReturningRunCallback, cancellationToken);

        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(operation);

        var report = exception.ToString();
        _ = await Assert.That(report).DoesNotContain(nameof(NullReferenceException));
        _ = await Assert.That(report).Contains("null", StringComparison.OrdinalIgnoreCase);
        _ = await Assert.That(report).Contains("callback", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies empty runs complete with empty replay schedules.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task RunAsyncCompletesNoWorkersEmptySchedule(CancellationToken cancellationToken)
    {
        var options = new RunOptions { Iterations = 1, Seed = 24, Schedule = ReplaySchedule.Replay() };
        await Runner.RunAsync(static _ => Task.CompletedTask, options, cancellationToken);
        _ = await Assert.That(options.Schedule.Steps).IsEmpty();
    }

    /// <summary>Verifies empty runs fail with non-empty replay schedules.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task RunAsyncFailsNoWorkersNonEmptySchedule(CancellationToken cancellationToken)
    {
        var operation = Runner.RunAsync(
            static _ => Task.CompletedTask,
            new RunOptions
            {
                Iterations = 1,
                Seed = 25,
                Schedule = ReplaySchedule.Replay(new ReplayStep("worker-1", "ready")),
            },
            cancellationToken);

        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(operation);

        var report = exception.ToString();
        _ = await Assert.That(report).Contains("unused steps", StringComparison.OrdinalIgnoreCase);
        _ = await Assert.That(report).Contains("Schedule:");
    }

    /// <summary>Verifies a scripted schedule with steps that no worker can satisfy after the run completes is reported as a failure.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task RunAsyncFailsWhenScheduleHasUnusedSteps(CancellationToken cancellationToken)
    {
        var options = new RunOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = ReplaySchedule.Replay(new ReplayStep("worker-1", "ready"), new ReplayStep("worker-2", "never")),
        };

        var operation = Runner.RunAsync(
            async context =>
            {
                context.Fork(async () => await Probe.HitAsync("ready", cancellationToken));

                await context.JoinAsync(cancellationToken);
            },
            options,
            cancellationToken);

        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(operation);

        _ = await Assert.That(exception.Message).Contains("Scripted schedule contained unused steps after all workers completed.");
    }

    /// <summary>Verifies probe-free workers complete with empty replay schedules.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task WorkerNoProbesCompletesEmptySchedule(CancellationToken cancellationToken)
    {
        var options = new RunOptions { Iterations = 1, Seed = 23, Schedule = ReplaySchedule.Replay() };
        await Runner.RunAsync(
            async context =>
            {
                context.Fork(static () => Task.CompletedTask);
                await context.JoinAsync(cancellationToken);
            },
            options,
            cancellationToken);

        _ = await Assert.That(options.Schedule.Steps).IsEmpty();
    }

    /// <summary>Verifies probe-free workers can complete without schedules.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task WorkerNoProbesCompletesWithoutSchedule(CancellationToken cancellationToken)
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

                return context.JoinAsync(cancellationToken);
            },
            new RunOptions { Iterations = 1, Seed = 21 },
            cancellationToken);

        _ = await Assert.That(counter).IsEqualTo(1);
    }

    /// <summary>Verifies probe-free workers fail when replay steps are configured.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task WorkerNoProbesFailsWhenProbeSteps(CancellationToken cancellationToken)
    {
        var operation = Runner.RunAsync(
            async context =>
            {
                context.Fork(static () => Task.CompletedTask);
                await context.JoinAsync(cancellationToken);
            },
            new RunOptions
            {
                Iterations = 1,
                Seed = 22,
                Schedule = ReplaySchedule.Replay(new ReplayStep("worker-1", "ready")),
            },
            cancellationToken);

        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(operation);

        var report = exception.ToString();
        _ = await Assert.That(report).Contains("unused steps", StringComparison.OrdinalIgnoreCase);
        _ = await Assert.That(report).Contains("worker-1 completed");
    }
}
