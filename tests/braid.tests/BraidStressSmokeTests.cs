using TUnit.Assertions.Enums;

namespace Braid.Tests;

/// <summary>Covers small scheduler stress smoke scenarios.</summary>
public sealed class BraidStressSmokeTests : TestBase
{
    /// <summary>Verifies many workers waiting at the same probe are all released.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task RunAsyncCompletesManyWorkersAtSameProbe(CancellationToken cancellationToken)
    {
        var completed = new CompletionCounter();

        await Runner.RunAsync(
            context =>
            {
                for (var index = 0; index < 20; index++)
                    ForkHitReadyAndIncrement(context, completed, cancellationToken);

                return context.JoinAsync(cancellationToken);
            },
            new RunOptions { Iterations = 1, Seed = 12345, Timeout = TimeSpan.FromSeconds(2) },
            cancellationToken);

        _ = await Assert.That(completed.Value).IsEqualTo(20);
    }

    /// <summary>Verifies multiple short iterations complete without leaking scheduler state.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task RunAsyncCompletesMultipleSmallWorkers(CancellationToken cancellationToken)
    {
        const int iterations = 10;
        const int workers = 5;
        var completed = new CompletionCounter();

        await Runner.RunAsync(
            context =>
            {
                for (var index = 0; index < workers; index++)
                    ForkHitReadyAndIncrement(context, completed, cancellationToken);

                return context.JoinAsync(cancellationToken);
            },
            new RunOptions { Iterations = iterations, Seed = 12345, Timeout = TimeSpan.FromSeconds(2) },
            cancellationToken);

        _ = await Assert.That(completed.Value).IsEqualTo(iterations * workers);
    }

    /// <summary>Verifies a scripted schedule can release several workers in reverse order.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task RunAsyncReplaysWorkersScriptedOrder(CancellationToken cancellationToken)
    {
        Lock gate = new();
        var releases = new List<string>();
        var options = new RunOptions
        {
            Iterations = 1,
            Seed = 12345,
            Timeout = TimeSpan.FromSeconds(2),
            Schedule = ReplaySchedule.Replay(
                new ReplayStep("worker-4", "ready"),
                new ReplayStep("worker-3", "ready"),
                new ReplayStep("worker-2", "ready"),
                new ReplayStep("worker-1", "ready")),
        };

        await Runner.RunAsync(
            context =>
            {
                for (var index = 0; index < 4; index++)
                    ForkHitReadyRecordWorker(context, $"worker-{index + 1}", releases, gate, cancellationToken);

                return context.JoinAsync(cancellationToken);
            },
            options,
            cancellationToken);

        _ = await Assert.That(releases).IsEquivalentTo(["worker-4", "worker-3", "worker-2", "worker-1"], CollectionOrdering.Matching);
    }
}
