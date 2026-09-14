using TUnit.Assertions.Enums;

namespace Braid.Tests;

/// <summary>Covers braid failure report formatting behavior.</summary>
public sealed class BraidFailureReportTests : TestBase
{
    /// <summary>Verifies inner exception details remain visible when replay text is present.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task FailureReportDoesNotLoseInnerException(CancellationToken cancellationToken)
    {
        var options = new RunOptions
        {
            Iterations = 1,
            Schedule = ReplaySchedule.Replay(ReplayStep.Hit("worker-1", "ready")),
        };

        var operation = Runner.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    await Probe.HitAsync("ready", cancellationToken);
                    throw new InvalidOperationException("inner-boom");
                });

                await context.JoinAsync(cancellationToken);
            },
            options,
            cancellationToken);
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(operation);

        _ = await Assert.That(exception.InnerException).IsNotNull();
        var report = exception.ToString();
        _ = await Assert.That(report).Contains("Replay text:");
        _ = await Assert.That(report).Contains("inner-boom");
        _ = await Assert.That(report).Contains("Inner exception:");
    }

    /// <summary>Verifies report formatting does not throw when replay text cannot be exported.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task FailureReportDoesNotThrowWhenNotRendered(CancellationToken cancellationToken)
    {
        var options = new RunOptions
        {
            Iterations = 1,
            Schedule = ReplaySchedule.Replay(ReplayStep.Hit("has space", "ready")),
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

        _ = await Assert.That(exception.ToString).ThrowsNothing();

        var report = exception.ToString();
        _ = await Assert.That(report).Contains("Replay text unavailable");
        _ = await Assert.That(report).Contains("cannot be represented");
    }

    /// <summary>Verifies arrive-held state is visible before a worker throws at a later scripted hit.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task FailureReportIncludesHeldBeforeRelease(CancellationToken cancellationToken)
    {
        var exception = await RunHeldWorkerFailureAsync("boom", cancellationToken);

        var report = exception.ToString();
        _ = await Assert.That(report).Contains("Held workers:", StringComparison.OrdinalIgnoreCase);
        _ = await Assert.That(report).Contains("worker-1");
        _ = await Assert.That(report).Contains("cache-hit");
        _ = await Assert.That(report).Contains("boom");
    }

    /// <summary>Verifies the last matched replay step is listed when a later step cannot run.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task FailureReportIncludesLastMatchedStep(CancellationToken cancellationToken)
    {
        var options = new RunOptions
        {
            Iterations = 1,
            Schedule = ReplaySchedule.Replay(ReplayStep.Hit("worker-1", "ready"), ReplayStep.Hit("worker-2", "later")),
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

        var report = exception.ToString();
        _ = await Assert.That(report).Contains("Last matched replay step:");
        _ = await Assert.That(report).Contains("hit worker-1 ready");
    }

    /// <summary>Verifies failure reports include replay text for arrive and release steps.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task FailureReportIncludesTextArriveRelease(CancellationToken cancellationToken)
    {
        var options = new RunOptions
        {
            Iterations = 1,
            Schedule = ReplaySchedule.Replay(ReplayStep.Arrive("worker-1", "cache-hit"), ReplayStep.Hit("worker-2", "mutation-done"), ReplayStep.Release("worker-1", "cache-hit")),
        };

        var operation = Runner.RunAsync(
            async context =>
            {
                context.Fork(async () => await Probe.HitAsync("cache-hit", cancellationToken));

                context.Fork(async () =>
                {
                    await Probe.HitAsync("mutation-done", cancellationToken);
                    throw new InvalidOperationException("boom");
                });

                await context.JoinAsync(cancellationToken);
            },
            options,
            cancellationToken);
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(operation);

        var report = exception.ToString();
        _ = await Assert.That(report).Contains("Replay text:");
        _ = await Assert.That(report).Contains("arrive worker-1 cache-hit");
        _ = await Assert.That(report).Contains("hit worker-2 mutation-done");
        _ = await Assert.That(report).Contains("release worker-1 cache-hit");
    }

    /// <summary>Verifies failure reports include canonical replay text for hit-only schedules.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task FailureReportIncludesTextHitSchedule(CancellationToken cancellationToken)
    {
        var options = new RunOptions
        {
            Iterations = 1,
            Schedule = ReplaySchedule.Replay(ReplayStep.Hit("worker-1", "ready")),
        };

        var operation = Runner.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    await Probe.HitAsync("ready", cancellationToken);
                    throw new InvalidOperationException("boom");
                });

                await context.JoinAsync(cancellationToken);
            },
            options,
            cancellationToken);
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(operation);

        var report = exception.ToString();
        _ = await Assert.That(report).Contains("Replay text:");
        _ = await Assert.That(report).Contains("hit worker-1 ready");
    }

    /// <summary>Verifies unused replay steps appear in scheduler diagnostics.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task FailureReportIncludesUnusedReplaySteps(CancellationToken cancellationToken)
    {
        var options = new RunOptions
        {
            Iterations = 1,
            Schedule = ReplaySchedule.Replay(ReplayStep.Hit("worker-1", "ready"), ReplayStep.Hit("worker-2", "never-hit")),
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

        var report = exception.ToString();
        _ = await Assert.That(report).Contains("Unused replay steps:");
        _ = await Assert.That(report).Contains("hit worker-2 never-hit");
    }

    /// <summary>Verifies waiting workers blocked at probes appear in diagnostics when another worker fails.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task FailureReportIncludesWaitingWorkers(CancellationToken cancellationToken)
    {
        var operation = Runner.RunAsync(
            async context =>
            {
                context.Fork(async () => await Probe.HitAsync("blocked", cancellationToken));

                context.Fork(async () =>
                {
                    await Probe.HitAsync("before-boom", cancellationToken);
                    throw new InvalidOperationException("boom");
                });

                await context.JoinAsync(cancellationToken);
            },
            new RunOptions { Iterations = 1, Seed = 1 },
            cancellationToken);
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(operation);

        var report = exception.ToString();
        _ = await Assert.That(report).Contains("Waiting workers:");
        _ = await Assert.That(report).Contains("worker-1");
        _ = await Assert.That(report).Contains("blocked");
    }

    /// <summary>Verifies scheduler diagnostics do not hide the inner exception message.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task FailureReportStateDoesNotHideInner(CancellationToken cancellationToken)
    {
        var exception = await RunHeldWorkerFailureAsync("inner-boom", cancellationToken);

        var report = exception.ToString();
        _ = await Assert.That(report).Contains("Last matched replay step:");
        _ = await Assert.That(report).Contains("inner-boom");
        _ = await Assert.That(report).Contains("Inner exception:");
    }

    /// <summary>Verifies replay text in the report matches <see cref="ReplaySchedule.ToReplayText" /> and parses back to the same steps.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task FailureReportTextParsesBackSchedule(CancellationToken cancellationToken)
    {
        var configured = ReplaySchedule.Replay(ReplayStep.Hit("worker-1", "after-read"), ReplayStep.Arrive("worker-2", "before-write"), ReplayStep.Release("worker-2", "before-write"));

        var options = new RunOptions
        {
            Iterations = 1,
            Schedule = configured,
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

        var expectedReplay = configured.ToReplayText();
        var report = exception.ToString();

        _ = await Assert.That(report).Contains("Replay text:");
        _ = await Assert.That(report).Contains(expectedReplay);

        var parsed = ReplaySchedule.Parse(expectedReplay);
        _ = await Assert.That(parsed.Steps.Count).IsEqualTo(configured.Steps.Count);
        for (var index = 0; index < configured.Steps.Count; index++)
        {
            _ = await Assert.That(parsed.Steps[index].Kind).IsEqualTo(configured.Steps[index].Kind);
            _ = await Assert.That(parsed.Steps[index].WorkerId).IsEqualTo(configured.Steps[index].WorkerId);
            _ = await Assert.That(parsed.Steps[index].ProbeName).IsEqualTo(configured.Steps[index].ProbeName);
        }
    }

    /// <summary>Verifies lost-update replay failures include schedule and trace details.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task RunAsyncReportsScheduleTraceLostUpdate(CancellationToken cancellationToken)
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
            async context =>
            {
                var value = 0;

                context.Fork(async () =>
                {
                    var current = value;
                    await Probe.HitAsync("after-read", cancellationToken);
                    await Probe.HitAsync("before-write", cancellationToken);
                    value = current + 1;
                });

                context.Fork(async () =>
                {
                    var current = value;
                    await Probe.HitAsync("after-read", cancellationToken);
                    await Probe.HitAsync("before-write", cancellationToken);
                    value = current + 1;
                });

                await context.JoinAsync(cancellationToken);

                _ = await Assert.That(value).IsEqualTo(2);
            },
            options,
            cancellationToken);
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(operation);

        var report = exception.ToString();
        _ = await Assert.That(report).Contains("Schedule:");
        _ = await Assert.That(report).Contains("Trace:");
        _ = await Assert.That(report).Contains("after-read");
        _ = await Assert.That(report).Contains("before-write");
        _ = await Assert.That(report).Contains("worker-1");
        _ = await Assert.That(report).Contains("worker-2");
    }

    /// <summary>Verifies scripted schedules appear in failure reports.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task RunAsyncReportsScriptedFailureOccurs(CancellationToken cancellationToken)
    {
        var options = new RunOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = ReplaySchedule.Replay(new ReplayStep("worker-1", "after-read"), new ReplayStep("worker-2", "after-read")),
        };

        var operation = Runner.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    await Probe.HitAsync("after-read", cancellationToken);
                    throw new InvalidOperationException("scripted boom");
                });

                context.Fork(async () => await Probe.HitAsync("after-read", cancellationToken));

                await context.JoinAsync(cancellationToken);
            },
            options,
            cancellationToken);
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(operation);

        var report = exception.ToString();
        _ = await Assert.That(exception.Steps).IsEquivalentTo(options.Schedule.Steps, CollectionOrdering.Matching);
        _ = await Assert.That(report).Contains("Schedule:");
        _ = await Assert.That(report).Contains("worker-1 @ after-read");
        _ = await Assert.That(report).Contains("worker-2 @ after-read");
    }

    /// <summary>Verifies failures include seed, iteration, trace, and inner message.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task RunAsyncReportsSeedTraceInterleaving(CancellationToken cancellationToken)
    {
        var operation = Runner.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    await Probe.HitAsync("before-failure", cancellationToken);
                    throw new InvalidOperationException("boom");
                });

                await context.JoinAsync(cancellationToken);
            },
            new RunOptions { Iterations = 1, Seed = 12345 },
            cancellationToken);
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(operation);

        _ = await Assert.That(exception.Seed).IsEqualTo(12345);
        var sawBeforeFailure = false;
        foreach (var line in exception.Traces)
        {
            if (!line.Contains("before-failure", StringComparison.Ordinal))
                continue;
            sawBeforeFailure = true;
            break;
        }

        _ = await Assert.That(sawBeforeFailure).IsTrue().Because("Trace should mention the before-failure marker.");
        var report = exception.ToString();
        _ = await Assert.That(report).Contains("Seed: 12345");
        _ = await Assert.That(report).Contains("Iteration:");
        _ = await Assert.That(report).Contains("Trace:");
        _ = await Assert.That(report).Contains("before-failure");
        _ = await Assert.That(report).Contains("boom");
    }

    /// <summary>Runs the arrive-hold-release schedule in which the held worker fails at the later scripted hit, returning the raised failure.</summary>
    /// <param name="innerMessage">The message of the exception thrown by the failing worker.</param>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>The raised <see cref="RunException" />.</returns>
    private static Task<RunException> RunHeldWorkerFailureAsync(string innerMessage, CancellationToken cancellationToken = default)
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
                context.Fork(async () => await Probe.HitAsync("cache-hit", cancellationToken));

                context.Fork(async () =>
                {
                    await Probe.HitAsync("fail-point", cancellationToken);
                    throw new InvalidOperationException(innerMessage);
                });

                await context.JoinAsync(cancellationToken);
            },
            options,
            cancellationToken);

        return BraidAssertions.AssertExpectsAsync<RunException>(operation);
    }
}
