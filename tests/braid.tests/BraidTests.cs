using Xunit;

namespace Braid.Tests;

/// <summary>
/// Covers braid run behavior.
/// </summary>
public sealed class BraidTests : TestBase
{
    /// <summary>
    /// Verifies a run completes after forked operations complete.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncCompletesWhenForksComplete()
    {
        var value = 0;

        await Braid.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    await BraidProbe.HitAsync("first", DefaultCancellationToken);
                    _ = Interlocked.Increment(ref value);
                });

                context.Fork(async () =>
                {
                    await BraidProbe.HitAsync("second", DefaultCancellationToken);
                    _ = Interlocked.Increment(ref value);
                });

                await context.JoinAsync(DefaultCancellationToken);
            },
            new BraidOptions { Iterations = 1, Seed = 12345 },
            DefaultCancellationToken);

        Assert.Equal(2, value);
    }

    /// <summary>
    /// Verifies failures include the seed and trace.
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
        Assert.Contains("Schedule:", report, StringComparison.Ordinal);
        Assert.Contains("worker-1 @ after-read", report, StringComparison.Ordinal);
        Assert.Contains("worker-2 @ after-read", report, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies scripted schedules release workers in the requested order.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncReleasesWorkersInScriptedOrder()
    {
        var releases = new List<string>();
        var options = new BraidOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = BraidSchedule.Replay(new BraidStep("worker-2", "ready"), new BraidStep("worker-1", "ready")),
        };

        await Braid.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    await BraidProbe.HitAsync("ready", DefaultCancellationToken);
                    releases.Add("worker-1");
                });

                context.Fork(async () =>
                {
                    await BraidProbe.HitAsync("ready", DefaultCancellationToken);
                    releases.Add("worker-2");
                });

                await context.JoinAsync(DefaultCancellationToken);
            },
            options,
            DefaultCancellationToken);

        Assert.Equal(["worker-2", "worker-1"], releases);
    }

    /// <summary>
    /// Verifies scripted schedules can reproduce a lost update.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncReplaysScriptedScheduleThatReproducesLostUpdate()
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

        Assert.Equal(12345, exception.Seed);
        Assert.Contains(exception.Trace, static line => line.Contains("worker-1", StringComparison.Ordinal));
        Assert.Contains(exception.Trace, static line => line.Contains("worker-2", StringComparison.Ordinal));
        Assert.Contains(exception.Trace, static line => line.Contains("after-read", StringComparison.Ordinal));
        Assert.Contains(exception.Trace, static line => line.Contains("before-write", StringComparison.Ordinal));

        var report = exception.ToString();
        Assert.Contains("Schedule:", report, StringComparison.Ordinal);
        Assert.Contains("Trace:", report, StringComparison.Ordinal);
        Assert.Contains("after-read", report, StringComparison.Ordinal);
        Assert.Contains("before-write", report, StringComparison.Ordinal);
        Assert.Contains("worker-1", report, StringComparison.Ordinal);
        Assert.Contains("worker-2", report, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies an unsatisfied scripted step fails with a clear report.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncFailsWhenScriptedStepCannotBeSatisfied()
    {
        var options = new BraidOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = BraidSchedule.Replay(new BraidStep("worker-2", "ready")),
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

        var report = exception.ToString();
        Assert.Contains("Scripted schedule step", report, StringComparison.Ordinal);
        Assert.Contains("worker-2", report, StringComparison.Ordinal);
        Assert.Contains("ready", report, StringComparison.Ordinal);
        Assert.Contains("Seed: 12345", report, StringComparison.Ordinal);
        Assert.Contains("Trace:", report, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies schedule exhaustion fails with a clear report.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncFailsWhenScriptedScheduleIsExhausted()
    {
        var options = new BraidOptions
        {
            Iterations = 1,
            Seed = 12345,
            Timeout = TimeSpan.FromMilliseconds(100),
            Schedule = BraidSchedule.Replay(new BraidStep("worker-1", "ready")),
        };

        var exception = await Assert.ThrowsAsync<BraidRunException>(async () =>
        {
            await Braid.RunAsync(
                static async context =>
                {
                    context.Fork(static async () => { await BraidProbe.HitAsync("ready", DefaultCancellationToken); });

                    context.Fork(static async () => { await BraidProbe.HitAsync("ready", DefaultCancellationToken); });

                    await context.JoinAsync(DefaultCancellationToken);
                },
                options,
                DefaultCancellationToken);
        });

        var report = exception.ToString();
        Assert.Contains("Scripted schedule was exhausted", report, StringComparison.Ordinal);
        Assert.Contains("Seed: 12345", report, StringComparison.Ordinal);
        Assert.Contains("Trace:", report, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies external cancellation unblocks a waiting braid run.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncSurfacesOperationCanceledWhenCanceledExternally()
    {
        using var cancellation = new CancellationTokenSource();
        var options = new BraidOptions
        {
            Iterations = 1,
            Seed = 12345,
            Timeout = TimeSpan.FromSeconds(5),
            Schedule = BraidSchedule.Replay(new BraidStep("worker-2", "ready")),
        };

        var runTask = Braid.RunAsync(
            async context =>
            {
                context.Fork(static async () => { await BraidProbe.HitAsync("ready", DefaultCancellationToken); });

                context.Fork(async () =>
                {
                    while (!cancellation.Token.IsCancellationRequested)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(5), cancellation.Token).ConfigureAwait(false);
                    }
                });

                await context.JoinAsync(cancellation.Token);
            },
            options,
            cancellation.Token);

        await Task.Delay(TimeSpan.FromMilliseconds(30), DefaultCancellationToken);
        cancellation.Cancel();

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await runTask);
    }

    /// <summary>
    /// Verifies timeout failures are reported as braid run exceptions.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncReportsTimeoutAsBraidRunException()
    {
        var options = new BraidOptions
        {
            Iterations = 1,
            Seed = 12345,
            Timeout = TimeSpan.FromMilliseconds(50),
        };

        var exception = await Assert.ThrowsAsync<BraidRunException>(async () =>
        {
            await Braid.RunAsync(
                static async context =>
                {
                    context.Fork(static async () => { await Task.Delay(TimeSpan.FromMilliseconds(200), DefaultCancellationToken); });

                    await context.JoinAsync(DefaultCancellationToken);
                },
                options,
                DefaultCancellationToken);
        });

        var report = exception.ToString();
        Assert.Contains("braid run timed out.", report, StringComparison.Ordinal);
        Assert.Contains("Seed: 12345", report, StringComparison.Ordinal);
        Assert.Contains("Trace:", report, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies probe behavior does not leak outside a failed run.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncClearsRunScopeAfterFailure()
    {
        _ = await Assert.ThrowsAsync<BraidRunException>(async () =>
        {
            await Braid.RunAsync(
                static async context =>
                {
                    context.Fork(static async () =>
                    {
                        await BraidProbe.HitAsync("before-failure", DefaultCancellationToken);
                        throw new InvalidOperationException("scope-failure");
                    });

                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions { Iterations = 1, Seed = 12345 },
                DefaultCancellationToken);
        });

        await BraidProbe.HitAsync("outside-run", DefaultCancellationToken);
    }
}
