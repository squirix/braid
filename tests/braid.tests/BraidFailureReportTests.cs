using Xunit;

namespace Braid.Tests;

/// <summary>Covers braid failure report formatting behavior.</summary>
public sealed class BraidFailureReportTests : TestBase
{
    /// <summary>Verifies inner exception details remain visible when replay text is present.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task FailureReportDoesNotLoseInnerException()
    {
        var options = new RunOptions
        {
            Iterations = 1,
            Schedule = ReplaySchedule.Replay(ReplayStep.Hit("worker-1", "ready")),
        };

        var operation = Runner.RunAsync(
            static async context =>
            {
                context.Fork(static async () =>
                {
                    await Probe.HitAsync("ready", DefaultCancellationToken);
                    throw new InvalidOperationException("inner-boom");
                });

                await context.JoinAsync(DefaultCancellationToken);
            },
            options,
            DefaultCancellationToken);
        var exception = await Assertions.ExpectsAsync<RunException>(operation);

        Assert.NotNull(exception.InnerException);
        var report = exception.ToString();
        Assert.Contains("Replay text:", report, StringComparison.Ordinal);
        Assert.Contains("inner-boom", report, StringComparison.Ordinal);
        Assert.Contains("Inner exception:", report, StringComparison.Ordinal);
    }

    /// <summary>Verifies report formatting does not throw when replay text cannot be exported.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task FailureReportDoesNotThrowWhenNotRendered()
    {
        var options = new RunOptions
        {
            Iterations = 1,
            Schedule = ReplaySchedule.Replay(ReplayStep.Hit("has space", "ready")),
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

        var reportEx = Record.Exception(exception.ToString);
        Assert.Null(reportEx);

        var report = exception.ToString();
        Assert.Contains("Replay text unavailable", report, StringComparison.Ordinal);
        Assert.Contains("cannot be represented", report, StringComparison.Ordinal);
    }

    /// <summary>Verifies arrive-held state is visible before a worker throws at a later scripted hit.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task FailureReportIncludesHeldBeforeRelease()
    {
        var exception = await RunHeldWorkerFailureAsync("boom");

        var report = exception.ToString();
        Assert.Contains("Held workers:", report, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("worker-1", report, StringComparison.Ordinal);
        Assert.Contains("cache-hit", report, StringComparison.Ordinal);
        Assert.Contains("boom", report, StringComparison.Ordinal);
    }

    /// <summary>Verifies the last matched replay step is listed when a later step cannot run.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task FailureReportIncludesLastMatchedStep()
    {
        var options = new RunOptions
        {
            Iterations = 1,
            Schedule = ReplaySchedule.Replay(ReplayStep.Hit("worker-1", "ready"), ReplayStep.Hit("worker-2", "later")),
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

        var report = exception.ToString();
        Assert.Contains("Last matched replay step:", report, StringComparison.Ordinal);
        Assert.Contains("hit worker-1 ready", report, StringComparison.Ordinal);
    }

    /// <summary>Verifies failure reports include replay text for arrive and release steps.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task FailureReportIncludesTextArriveRelease()
    {
        var options = new RunOptions
        {
            Iterations = 1,
            Schedule = ReplaySchedule.Replay(ReplayStep.Arrive("worker-1", "cache-hit"), ReplayStep.Hit("worker-2", "mutation-done"), ReplayStep.Release("worker-1", "cache-hit")),
        };

        var operation = Runner.RunAsync(
            static async context =>
            {
                context.Fork(static async () => await Probe.HitAsync("cache-hit", DefaultCancellationToken));

                context.Fork(static async () =>
                {
                    await Probe.HitAsync("mutation-done", DefaultCancellationToken);
                    throw new InvalidOperationException("boom");
                });

                await context.JoinAsync(DefaultCancellationToken);
            },
            options,
            DefaultCancellationToken);
        var exception = await Assertions.ExpectsAsync<RunException>(operation);

        var report = exception.ToString();
        Assert.Contains("Replay text:", report, StringComparison.Ordinal);
        Assert.Contains("arrive worker-1 cache-hit", report, StringComparison.Ordinal);
        Assert.Contains("hit worker-2 mutation-done", report, StringComparison.Ordinal);
        Assert.Contains("release worker-1 cache-hit", report, StringComparison.Ordinal);
    }

    /// <summary>Verifies failure reports include canonical replay text for hit-only schedules.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task FailureReportIncludesTextHitSchedule()
    {
        var options = new RunOptions
        {
            Iterations = 1,
            Schedule = ReplaySchedule.Replay(ReplayStep.Hit("worker-1", "ready")),
        };

        var operation = Runner.RunAsync(
            static async context =>
            {
                context.Fork(static async () =>
                {
                    await Probe.HitAsync("ready", DefaultCancellationToken);
                    throw new InvalidOperationException("boom");
                });

                await context.JoinAsync(DefaultCancellationToken);
            },
            options,
            DefaultCancellationToken);
        var exception = await Assertions.ExpectsAsync<RunException>(operation);

        var report = exception.ToString();
        Assert.Contains("Replay text:", report, StringComparison.Ordinal);
        Assert.Contains("hit worker-1 ready", report, StringComparison.Ordinal);
    }

    /// <summary>Verifies unused replay steps appear in scheduler diagnostics.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task FailureReportIncludesUnusedReplaySteps()
    {
        var options = new RunOptions
        {
            Iterations = 1,
            Schedule = ReplaySchedule.Replay(ReplayStep.Hit("worker-1", "ready"), ReplayStep.Hit("worker-2", "never-hit")),
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

        var report = exception.ToString();
        Assert.Contains("Unused replay steps:", report, StringComparison.Ordinal);
        Assert.Contains("hit worker-2 never-hit", report, StringComparison.Ordinal);
    }

    /// <summary>Verifies waiting workers blocked at probes appear in diagnostics when another worker fails.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task FailureReportIncludesWaitingWorkers()
    {
        var operation = Runner.RunAsync(
            static async context =>
            {
                context.Fork(static async () => await Probe.HitAsync("blocked", DefaultCancellationToken));

                context.Fork(static async () =>
                {
                    await Probe.HitAsync("before-boom", DefaultCancellationToken);
                    throw new InvalidOperationException("boom");
                });

                await context.JoinAsync(DefaultCancellationToken);
            },
            new RunOptions { Iterations = 1, Seed = 1 },
            DefaultCancellationToken);
        var exception = await Assertions.ExpectsAsync<RunException>(operation);

        var report = exception.ToString();
        Assert.Contains("Waiting workers:", report, StringComparison.Ordinal);
        Assert.Contains("worker-1", report, StringComparison.Ordinal);
        Assert.Contains("blocked", report, StringComparison.Ordinal);
    }

    /// <summary>Verifies scheduler diagnostics do not hide the inner exception message.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task FailureReportStateDoesNotHideInner()
    {
        var exception = await RunHeldWorkerFailureAsync("inner-boom");

        var report = exception.ToString();
        Assert.Contains("Last matched replay step:", report, StringComparison.Ordinal);
        Assert.Contains("inner-boom", report, StringComparison.Ordinal);
        Assert.Contains("Inner exception:", report, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies replay text in the report matches <see cref="ReplaySchedule.ToReplayText" /> and parses back to the same steps.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task FailureReportTextParsesBackSchedule()
    {
        var configured = ReplaySchedule.Replay(ReplayStep.Hit("worker-1", "after-read"), ReplayStep.Arrive("worker-2", "before-write"), ReplayStep.Release("worker-2", "before-write"));

        var options = new RunOptions
        {
            Iterations = 1,
            Schedule = configured,
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

        var expectedReplay = configured.ToReplayText();
        var report = exception.ToString();

        Assert.Contains("Replay text:", report, StringComparison.Ordinal);
        Assert.Contains(expectedReplay, report, StringComparison.Ordinal);

        var parsed = ReplaySchedule.Parse(expectedReplay);
        Assert.Equal(configured.Steps.Count, parsed.Steps.Count);
        for (var index = 0; index < configured.Steps.Count; index++)
        {
            Assert.Equal(configured.Steps[index].Kind, parsed.Steps[index].Kind);
            Assert.Equal(configured.Steps[index].WorkerId, parsed.Steps[index].WorkerId);
            Assert.Equal(configured.Steps[index].ProbeName, parsed.Steps[index].ProbeName);
        }
    }

    /// <summary>Verifies lost-update replay failures include schedule and trace details.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncReportsScheduleTraceLostUpdate()
    {
        var options = new RunOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = ReplaySchedule.Replay(
                new ReplayStep("worker-1", "after-read"),
                new ReplayStep("worker-2", "after-read"),
                new ReplayStep("worker-1", "before-write"),
                new ReplayStep("worker-2", "before-write")),
        };

        var operation = Runner.RunAsync(
            static async context =>
            {
                var value = 0;

                context.Fork(async () =>
                {
                    var current = value;
                    await Probe.HitAsync("after-read", DefaultCancellationToken);
                    await Probe.HitAsync("before-write", DefaultCancellationToken);
                    value = current + 1;
                });

                context.Fork(async () =>
                {
                    var current = value;
                    await Probe.HitAsync("after-read", DefaultCancellationToken);
                    await Probe.HitAsync("before-write", DefaultCancellationToken);
                    value = current + 1;
                });

                await context.JoinAsync(DefaultCancellationToken);

                Assert.Equal(2, value);
            },
            options,
            DefaultCancellationToken);
        var exception = await Assertions.ExpectsAsync<RunException>(operation);

        var report = exception.ToString();
        Assert.Contains("Schedule:", report, StringComparison.Ordinal);
        Assert.Contains("Trace:", report, StringComparison.Ordinal);
        Assert.Contains("after-read", report, StringComparison.Ordinal);
        Assert.Contains("before-write", report, StringComparison.Ordinal);
        Assert.Contains("worker-1", report, StringComparison.Ordinal);
        Assert.Contains("worker-2", report, StringComparison.Ordinal);
    }

    /// <summary>Verifies scripted schedules appear in failure reports.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncReportsScriptedFailureOccurs()
    {
        var options = new RunOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = ReplaySchedule.Replay(new ReplayStep("worker-1", "after-read"), new ReplayStep("worker-2", "after-read")),
        };

        var operation = Runner.RunAsync(
            static async context =>
            {
                context.Fork(static async () =>
                {
                    await Probe.HitAsync("after-read", DefaultCancellationToken);
                    throw new InvalidOperationException("scripted boom");
                });

                context.Fork(static async () => await Probe.HitAsync("after-read", DefaultCancellationToken));

                await context.JoinAsync(DefaultCancellationToken);
            },
            options,
            DefaultCancellationToken);
        var exception = await Assertions.ExpectsAsync<RunException>(operation);

        var report = exception.ToString();
        Assert.Equal(options.Schedule.Steps, exception.Schedule);
        Assert.Contains("Schedule:", report, StringComparison.Ordinal);
        Assert.Contains("worker-1 @ after-read", report, StringComparison.Ordinal);
        Assert.Contains("worker-2 @ after-read", report, StringComparison.Ordinal);
    }

    /// <summary>Verifies failures include seed, iteration, trace, and inner message.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncReportsSeedTraceInterleaving()
    {
        var operation = Runner.RunAsync(
            static async context =>
            {
                context.Fork(static async () =>
                {
                    await Probe.HitAsync("before-failure", DefaultCancellationToken);
                    throw new InvalidOperationException("boom");
                });

                await context.JoinAsync(DefaultCancellationToken);
            },
            new RunOptions { Iterations = 1, Seed = 12345 },
            DefaultCancellationToken);
        var exception = await Assertions.ExpectsAsync<RunException>(operation);

        Assert.Equal(12345, exception.Seed);
        var sawBeforeFailure = false;
        foreach (var line in exception.Trace)
        {
            if (!line.Contains("before-failure", StringComparison.Ordinal))
                continue;
            sawBeforeFailure = true;
            break;
        }

        Assert.True(sawBeforeFailure, "Trace should mention the before-failure marker.");
        var report = exception.ToString();
        Assert.Contains("Seed: 12345", report, StringComparison.Ordinal);
        Assert.Contains("Iteration:", report, StringComparison.Ordinal);
        Assert.Contains("Trace:", report, StringComparison.Ordinal);
        Assert.Contains("before-failure", report, StringComparison.Ordinal);
        Assert.Contains("boom", report, StringComparison.Ordinal);
    }

    /// <summary>Runs the arrive-hold-release schedule in which the held worker fails at the later scripted hit, returning the raised failure.</summary>
    /// <param name="innerMessage">The message of the exception thrown by the failing worker.</param>
    /// <returns>The raised <see cref="RunException" />.</returns>
    private static Task<RunException> RunHeldWorkerFailureAsync(string innerMessage)
    {
        var options = new RunOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = ReplaySchedule.Replay(ReplayStep.Arrive("worker-1", "cache-hit"), ReplayStep.Hit("worker-2", "fail-point"), ReplayStep.Release("worker-1", "cache-hit")),
        };

        var operation = Runner.RunAsync(
            async context =>
            {
                context.Fork(static async () => await Probe.HitAsync("cache-hit", DefaultCancellationToken));

                context.Fork(async () =>
                {
                    await Probe.HitAsync("fail-point", DefaultCancellationToken);
                    throw new InvalidOperationException(innerMessage);
                });

                await context.JoinAsync(DefaultCancellationToken);
            },
            options,
            DefaultCancellationToken);

        return Assertions.ExpectsAsync<RunException>(operation);
    }
}
