using Xunit;

namespace Braid.Examples.CancellationBeforeObservation;

/// <summary>
/// Demonstrates a cancellation race where a cancelled operation must not be counted as observed.
/// </summary>
public sealed class CancellationBeforeObservationTests
{
    private static CancellationToken TestCancellationToken => TestContext.Current.CancellationToken;

    /// <summary>
    /// Verifies cancellation wins before the observer records the operation.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task CancelledOperationIsNotObservedWhenCancellationWinsFirst()
    {
        var options = new BraidOptions
        {
            Iterations = 1,
            Schedule = BraidSchedule.Parse(
                """
                hit worker-2 cancelled
                hit worker-1 before-observe
                """),
        };

        var observed = await RunScenarioAsync(options);

        Assert.False(observed);
    }

    private static async Task<bool> RunScenarioAsync(BraidOptions options)
    {
        var operationCancelled = false;
        var observed = false;

        await Braid.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    await BraidProbe.HitAsync("before-observe", TestCancellationToken);

                    if (!operationCancelled)
                    {
                        observed = true;
                    }
                });

                context.Fork(async () =>
                {
                    operationCancelled = true;
                    await BraidProbe.HitAsync("cancelled", TestCancellationToken);
                });

                await context.JoinAsync(TestCancellationToken);
            },
            options,
            TestCancellationToken);

        return observed;
    }
}
