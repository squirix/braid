using Xunit;

namespace Braid.Tests;

/// <summary>Covers small scheduler stress smoke scenarios.</summary>
public sealed class BraidStressSmokeTests : TestBase
{
    /// <summary>Verifies many workers waiting at the same probe are all released.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncCompletesManyWorkersAtSameProbe()
    {
        var completed = new CompletionCounter();

        await Runner.RunAsync(
            context =>
            {
                for (var index = 0; index < 20; index++)
                    ForkHitReadyAndIncrement(context, completed);

                return context.JoinAsync(DefaultCancellationToken);
            },
            new RunOptions { Iterations = 1, Seed = 12345, Timeout = TimeSpan.FromSeconds(2) },
            DefaultCancellationToken);

        Assert.Equal(20, completed.Value);
    }

    /// <summary>Verifies multiple short iterations complete without leaking scheduler state.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncCompletesMultipleSmallWorkers()
    {
        const int iterations = 10;
        const int workers = 5;
        var completed = new CompletionCounter();

        await Runner.RunAsync(
            context =>
            {
                for (var index = 0; index < workers; index++)
                    ForkHitReadyAndIncrement(context, completed);

                return context.JoinAsync(DefaultCancellationToken);
            },
            new RunOptions { Iterations = iterations, Seed = 12345, Timeout = TimeSpan.FromSeconds(2) },
            DefaultCancellationToken);

        Assert.Equal(iterations * workers, completed.Value);
    }

    /// <summary>Verifies a scripted schedule can release several workers in reverse order.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncReplaysWorkersScriptedOrder()
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
                    ForkHitReadyRecordWorker(context, $"worker-{index + 1}", releases, gate);

                return context.JoinAsync(DefaultCancellationToken);
            },
            options,
            DefaultCancellationToken);

        Assert.Equal(["worker-4", "worker-3", "worker-2", "worker-1"], releases);
    }
}
