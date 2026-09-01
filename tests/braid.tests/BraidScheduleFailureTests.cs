using Xunit;

namespace Braid.Tests;

/// <summary>Covers scripted schedule failure behavior.</summary>
public sealed class BraidScheduleFailureTests : TestBase
{
    /// <summary>Verifies schedule exhaustion fails with a clear report.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncFailsWhenScheduleExhausted()
    {
        var options = new RunOptions
        {
            Iterations = 1,
            Seed = 12345,
            Timeout = TimeSpan.FromMilliseconds(100),
            Schedule = ReplaySchedule.Replay(new ReplayStep("worker-1", "ready")),
        };

        var exception = await Assertions.ExpectsAsync<RunException>(
            Runner.RunAsync(
                static async context =>
                {
                    context.Fork(static async () => await Probe.HitAsync("ready", DefaultCancellationToken));

                    context.Fork(static async () => await Probe.HitAsync("ready", DefaultCancellationToken));

                    await context.JoinAsync(DefaultCancellationToken);
                },
                options,
                DefaultCancellationToken));

        var report = exception.ToString();
        Assert.Contains("Scripted schedule was exhausted", report, StringComparison.Ordinal);
        Assert.Contains("Seed: 12345", report, StringComparison.Ordinal);
        Assert.Contains("Trace:", report, StringComparison.Ordinal);
    }

    /// <summary>Verifies an unsatisfied scripted step fails with a clear report.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncFailsWhenStepCannotBeMet()
    {
        var options = new RunOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = ReplaySchedule.Replay(new ReplayStep("worker-2", "ready")),
        };

        var exception = await Assertions.ExpectsAsync<RunException>(
            Runner.RunAsync(
                static async context =>
                {
                    context.Fork(static async () => await Probe.HitAsync("ready", DefaultCancellationToken));

                    await context.JoinAsync(DefaultCancellationToken);
                },
                options,
                DefaultCancellationToken));

        var report = exception.ToString();
        Assert.Contains("Scripted schedule step", report, StringComparison.Ordinal);
        Assert.Contains("worker-2", report, StringComparison.Ordinal);
        Assert.Contains("ready", report, StringComparison.Ordinal);
        Assert.Contains("Seed: 12345", report, StringComparison.Ordinal);
        Assert.Contains("Trace:", report, StringComparison.Ordinal);
    }
}
