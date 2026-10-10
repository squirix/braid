namespace Braid.Tests;

/// <summary>
/// Covers a callback that returns a null task. It happens inside the run, so it is reported like other misuse inside a run:
/// as a <see cref="RunException" /> that carries the seed and the iteration, not as a bare exception.
/// </summary>
public sealed class NullCallbackTaskTests : TestBase
{
    private const string NullTaskMessage = "Braid run callback returned a null task.";

    /// <summary>Verifies a run callback that returns null fails the run as a user test failure with the cause inside.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task RunCallbackReturningNullFailsTheRun(CancellationToken cancellationToken)
    {
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(Runner.RunAsync(NullTestValues.NullReturningRunCallback, cancellationToken));

        _ = await Assert.That(exception.Message).StartsWith("braid run failed.");
        _ = await Assert.That(exception.FailureOrigin).IsEqualTo(RunFailureOrigin.UserTest);
        _ = await Assert.That(exception.InnerException).IsTypeOf<InvalidOperationException>();
        _ = await Assert.That(exception.InnerException!.Message).IsEqualTo(NullTaskMessage);
    }

    /// <summary>Verifies the failure names the iteration and the seed in which the callback returned null.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task NullInLaterIterationNamesIteration(CancellationToken cancellationToken)
    {
        var calls = 0;
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                _ => Interlocked.Increment(ref calls) == 3 ? NullTestValues.NullTask : Task.CompletedTask,
                new RunOptions { Iterations = 5, Seed = 100 },
                cancellationToken));

        _ = await Assert.That(exception.Iteration).IsEqualTo(2);
        _ = await Assert.That(exception.Seed).IsEqualTo(102);
        _ = await Assert.That(exception.InnerException!.Message).IsEqualTo(NullTaskMessage);
        _ = await Assert.That(calls).IsEqualTo(3);
    }

    /// <summary>Verifies an exploration callback that returns null fails exploration the same way.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ExploreCallbackReturningNullFails(CancellationToken cancellationToken)
    {
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.ExploreAsync(static options => options.WithSeed(1), NullTestValues.NullReturningExploreCallback, cancellationToken));

        _ = await Assert.That(exception.FailureOrigin).IsEqualTo(RunFailureOrigin.UserTest);
        _ = await Assert.That(exception.InnerException).IsTypeOf<InvalidOperationException>();
        _ = await Assert.That(exception.InnerException!.Message).IsEqualTo(NullTaskMessage);
    }
}
