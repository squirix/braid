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
}
