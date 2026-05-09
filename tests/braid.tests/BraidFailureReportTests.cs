using Xunit;

namespace Braid.Tests;

/// <summary>
/// Covers braid failure report formatting behavior.
/// </summary>
public sealed class BraidFailureReportTests : TestBase
{
    /// <summary>
    /// Verifies lost-update replay failures include schedule and trace details.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncReportsScheduleAndTraceForLostUpdateReplayFailure()
    {
        var options = new BraidOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = BraidSchedule.Replay(
                new BraidStep("worker-1", "after-read"),
                new BraidStep("worker-2", "after-read"),
                new BraidStep("worker-1", "before-write"),
                new BraidStep("worker-2", "before-write")),
        };

        var exception = await Assert.ThrowsAsync<BraidRunException>(async () =>
        {
            await Braid.RunAsync(
                static async context =>
                {
                    var value = 0;

                    context.Fork(async () =>
                    {
                        var current = value;
                        await BraidProbe.HitAsync("after-read", DefaultCancellationToken);
                        await BraidProbe.HitAsync("before-write", DefaultCancellationToken);
                        value = current + 1;
                    });

                    context.Fork(async () =>
                    {
                        var current = value;
                        await BraidProbe.HitAsync("after-read", DefaultCancellationToken);
                        await BraidProbe.HitAsync("before-write", DefaultCancellationToken);
                        value = current + 1;
                    });

                    await context.JoinAsync(DefaultCancellationToken);

                    Assert.Equal(2, value);
                },
                options,
                DefaultCancellationToken);
        });

        var report = exception.ToString();
        Assert.Contains("Schedule:", report, StringComparison.Ordinal);
        Assert.Contains("Trace:", report, StringComparison.Ordinal);
        Assert.Contains("after-read", report, StringComparison.Ordinal);
        Assert.Contains("before-write", report, StringComparison.Ordinal);
        Assert.Contains("worker-1", report, StringComparison.Ordinal);
        Assert.Contains("worker-2", report, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies scripted schedules appear in failure reports.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncReportsScriptedScheduleWhenFailureOccurs()
    {
        var options = new BraidOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = BraidSchedule.Replay(new BraidStep("worker-1", "after-read"), new BraidStep("worker-2", "after-read")),
        };

        var exception = await Assert.ThrowsAsync<BraidRunException>(async () =>
        {
            await Braid.RunAsync(
                static async context =>
                {
                    context.Fork(static async () =>
                    {
                        await BraidProbe.HitAsync("after-read", DefaultCancellationToken);
                        throw new InvalidOperationException("scripted boom");
                    });

                    context.Fork(static async () => { await BraidProbe.HitAsync("after-read", DefaultCancellationToken); });

                    await context.JoinAsync(DefaultCancellationToken);
                },
                options,
                DefaultCancellationToken);
        });

        var report = exception.ToString();
        Assert.Equal(options.Schedule.Steps, exception.Schedule);
        Assert.Contains("Schedule:", report, StringComparison.Ordinal);
        Assert.Contains("worker-1 @ after-read", report, StringComparison.Ordinal);
        Assert.Contains("worker-2 @ after-read", report, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies failures include seed, iteration, trace, and inner message.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncReportsSeedAndTraceWhenInterleavingFails()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await Braid.RunAsync(
                static async context =>
                {
                    context.Fork(static async () =>
                    {
                        await BraidProbe.HitAsync("before-failure", DefaultCancellationToken);
                        throw new InvalidOperationException("boom");
                    });

                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions { Iterations = 1, Seed = 12345 },
                DefaultCancellationToken);
        });

        Assert.Equal(12345, exception.Seed);
        Assert.Contains(exception.Trace, static line => line.Contains("before-failure", StringComparison.Ordinal));
        var report = exception.ToString();
        Assert.Contains("Seed: 12345", report, StringComparison.Ordinal);
        Assert.Contains("Iteration:", report, StringComparison.Ordinal);
        Assert.Contains("Trace:", report, StringComparison.Ordinal);
        Assert.Contains("before-failure", report, StringComparison.Ordinal);
        Assert.Contains("boom", report, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies failure reports include canonical replay text for hit-only schedules.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task FailureReportIncludesReplayTextForHitSchedule()
    {
        var options = new BraidOptions
        {
            Iterations = 1,
            Schedule = BraidSchedule.Replay(BraidStep.Hit("worker-1", "ready")),
        };

        var exception = await Assert.ThrowsAsync<BraidRunException>(async () =>
        {
            await Braid.RunAsync(
                static async context =>
                {
                    context.Fork(static async () =>
                    {
                        await BraidProbe.HitAsync("ready", DefaultCancellationToken);
                        throw new InvalidOperationException("boom");
                    });

                    await context.JoinAsync(DefaultCancellationToken);
                },
                options,
                DefaultCancellationToken);
        });

        var report = exception.ToString();
        Assert.Contains("Replay text:", report, StringComparison.Ordinal);
        Assert.Contains("hit worker-1 ready", report, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies failure reports include replay text for arrive and release steps.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task FailureReportIncludesReplayTextForArriveReleaseSchedule()
    {
        var options = new BraidOptions
        {
            Iterations = 1,
            Schedule = BraidSchedule.Replay(
                BraidStep.Arrive("worker-1", "cache-hit"),
                BraidStep.Hit("worker-2", "mutation-done"),
                BraidStep.Release("worker-1", "cache-hit")),
        };

        var exception = await Assert.ThrowsAsync<BraidRunException>(async () =>
        {
            await Braid.RunAsync(
                static async context =>
                {
                    context.Fork(static async () =>
                    {
                        await BraidProbe.HitAsync("cache-hit", DefaultCancellationToken);
                    });

                    context.Fork(static async () =>
                    {
                        await BraidProbe.HitAsync("mutation-done", DefaultCancellationToken);
                        throw new InvalidOperationException("boom");
                    });

                    await context.JoinAsync(DefaultCancellationToken);
                },
                options,
                DefaultCancellationToken);
        });

        var report = exception.ToString();
        Assert.Contains("Replay text:", report, StringComparison.Ordinal);
        Assert.Contains("arrive worker-1 cache-hit", report, StringComparison.Ordinal);
        Assert.Contains("hit worker-2 mutation-done", report, StringComparison.Ordinal);
        Assert.Contains("release worker-1 cache-hit", report, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies replay text in the report matches <see cref="BraidSchedule.ToReplayText"/> and parses back to the same steps.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task FailureReportReplayTextParsesBackToSchedule()
    {
        var configured = BraidSchedule.Replay(
            BraidStep.Hit("worker-1", "after-read"),
            BraidStep.Arrive("worker-2", "before-write"),
            BraidStep.Release("worker-2", "before-write"));

        var options = new BraidOptions
        {
            Iterations = 1,
            Schedule = configured,
        };

        var exception = await Assert.ThrowsAsync<BraidRunException>(async () =>
        {
            await Braid.RunAsync(
                static async context =>
                {
                    context.Fork(static async () => { await BraidProbe.HitAsync("after-read", DefaultCancellationToken); });

                    await context.JoinAsync(DefaultCancellationToken);
                },
                options,
                DefaultCancellationToken);
        });

        var expectedReplay = configured.ToReplayText();
        var report = exception.ToString();

        Assert.Contains("Replay text:", report, StringComparison.Ordinal);
        Assert.Contains(expectedReplay, report, StringComparison.Ordinal);

        var parsed = BraidSchedule.Parse(expectedReplay);
        Assert.Equal(configured.Steps.Count, parsed.Steps.Count);
        for (var index = 0; index < configured.Steps.Count; index++)
        {
            Assert.Equal(configured.Steps[index].Kind, parsed.Steps[index].Kind);
            Assert.Equal(configured.Steps[index].WorkerId, parsed.Steps[index].WorkerId);
            Assert.Equal(configured.Steps[index].ProbeName, parsed.Steps[index].ProbeName);
        }
    }

    /// <summary>
    /// Verifies inner exception details remain visible when replay text is present.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task FailureReportDoesNotLoseInnerException()
    {
        var options = new BraidOptions
        {
            Iterations = 1,
            Schedule = BraidSchedule.Replay(BraidStep.Hit("worker-1", "ready")),
        };

        var exception = await Assert.ThrowsAsync<BraidRunException>(async () =>
        {
            await Braid.RunAsync(
                static async context =>
                {
                    context.Fork(static async () =>
                    {
                        await BraidProbe.HitAsync("ready", DefaultCancellationToken);
                        throw new InvalidOperationException("inner-boom");
                    });

                    await context.JoinAsync(DefaultCancellationToken);
                },
                options,
                DefaultCancellationToken);
        });

        Assert.NotNull(exception.InnerException);
        var report = exception.ToString();
        Assert.Contains("Replay text:", report, StringComparison.Ordinal);
        Assert.Contains("inner-boom", report, StringComparison.Ordinal);
        Assert.Contains("Inner exception:", report, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies report formatting does not throw when replay text cannot be exported.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task FailureReportDoesNotThrowWhenReplayTextCannotBeRendered()
    {
        var options = new BraidOptions
        {
            Iterations = 1,
            Schedule = BraidSchedule.Replay(BraidStep.Hit("has space", "ready")),
        };

        var exception = await Assert.ThrowsAsync<BraidRunException>(async () =>
        {
            await Braid.RunAsync(
                static async context =>
                {
                    context.Fork(static async () => { await BraidProbe.HitAsync("ready", DefaultCancellationToken); });

                    await context.JoinAsync(DefaultCancellationToken);
                },
                options,
                DefaultCancellationToken);
        });

        var reportEx = Record.Exception(() => exception.ToString());
        Assert.Null(reportEx);

        var report = exception.ToString();
        Assert.Contains("Replay text unavailable", report, StringComparison.Ordinal);
        Assert.Contains("cannot be represented", report, StringComparison.Ordinal);
    }
}
