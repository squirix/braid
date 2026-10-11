namespace Braid.Tests;

/// <summary>Covers scripted schedule failure behavior.</summary>
public sealed class BraidScheduleFailureTests : TestBase
{
    /// <summary>Verifies schedule exhaustion fails with a clear report.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task RunAsyncFailsWhenScheduleExhausted(CancellationToken cancellationToken)
    {
        var options = new RunOptions
        {
            Iterations = 1,
            Seed = 12345,
            Timeout = TimeSpan.FromMilliseconds(100),
            Schedule = ReplaySchedule.Replay(new ReplayStep("worker-1", "ready")),
        };

        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                async context =>
                {
                    context.Fork(async () => await Probe.HitAsync("ready", cancellationToken));

                    context.Fork(async () => await Probe.HitAsync("ready", cancellationToken));

                    await context.JoinAsync(cancellationToken);
                },
                options,
                cancellationToken));

        var report = exception.ToString();
        _ = await Assert.That(report).Contains("Scripted schedule was exhausted");
        _ = await Assert.That(report).Contains("Seed: 12345");
        _ = await Assert.That(report).Contains("Trace:");
    }

    /// <summary>Verifies an unsatisfied scripted step fails with a clear report.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task RunAsyncFailsWhenStepCannotBeMet(CancellationToken cancellationToken)
    {
        var options = new RunOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = ReplaySchedule.Replay(new ReplayStep("worker-2", "ready")),
        };

        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                async context =>
                {
                    context.Fork(async () => await Probe.HitAsync("ready", cancellationToken));

                    await context.JoinAsync(cancellationToken);
                },
                options,
                cancellationToken));

        var report = exception.ToString();
        _ = await Assert.That(report).Contains("Scripted schedule step");
        _ = await Assert.That(report).Contains("worker-2");
        _ = await Assert.That(report).Contains("ready");
        _ = await Assert.That(report).Contains("Seed: 12345");
        _ = await Assert.That(report).Contains("Trace:");
    }
}
