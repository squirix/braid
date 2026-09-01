#:sdk Microsoft.NET.Sdk
#:property PublishAot=false
#:project ../../../src/braid/Braid.csproj
#:package xunit.v3@3.2.2
#:package Microsoft.NET.Test.Sdk@18.7.0

using Xunit;

namespace Braid.Examples.CancellationBeforeObservation;

/// <summary>Demonstrates a cancellation race where a cancelled operation must not be counted as observed.</summary>
public sealed class CancellationBeforeObservationTests
{
    private static CancellationToken TestCancellationToken => TestContext.Current.CancellationToken;

    /// <summary>Verifies cancellation wins before the observer records the operation.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task CancelledOperationIsNotObservedWhenCancellationWinsFirst()
    {
        var options = new RunOptions
        {
            Iterations = 1,
            Schedule = ReplaySchedule.Parse("hit worker-2 cancelled\nhit worker-1 before-observe\n"),
        };

        var observed = await RunScenarioAsync(options);

        Assert.False(observed);
    }

    private static async Task<bool> RunScenarioAsync(RunOptions options)
    {
        var operationCancelled = false;
        var observed = false;

        await Runner.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    await Probe.HitAsync("before-observe", TestCancellationToken);

                    if (!operationCancelled)
                    {
                        observed = true;
                    }
                });

                context.Fork(async () =>
                {
                    operationCancelled = true;
                    await Probe.HitAsync("cancelled", TestCancellationToken);
                });

                await context.JoinAsync(TestCancellationToken);
            },
            options,
            TestCancellationToken);

        return observed;
    }
}
