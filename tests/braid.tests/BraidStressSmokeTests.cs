using Xunit;

namespace Braid.Tests;

/// <summary>
/// Covers small scheduler stress smoke scenarios.
/// </summary>
public sealed class BraidStressSmokeTests : TestBase
{
    /// <summary>
    /// Verifies many workers waiting at the same probe are all released.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncCompletesManyWorkersAtSameProbe()
    {
        var completed = 0;

        await Braid.RunAsync(
            context =>
            {
                for (var index = 0; index < 20; index++)
                {
                    context.Fork(async () =>
                    {
                        await BraidProbe.HitAsync("ready", DefaultCancellationToken);
                        _ = Interlocked.Increment(ref completed);
                    });
                }

                return context.JoinAsync(DefaultCancellationToken);
            },
            new BraidOptions { Iterations = 1, Seed = 12345, Timeout = TimeSpan.FromSeconds(2) },
            DefaultCancellationToken);

        Assert.Equal(20, completed);
    }

    /// <summary>
    /// Verifies multiple short iterations complete without leaking scheduler state.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncCompletesMultipleIterationsWithSmallWorkers()
    {
        const int iterations = 10;
        const int workers = 5;
        var completed = 0;

        await Braid.RunAsync(
            context =>
            {
                for (var index = 0; index < workers; index++)
                {
                    context.Fork(async () =>
                    {
                        await BraidProbe.HitAsync("ready", DefaultCancellationToken);
                        _ = Interlocked.Increment(ref completed);
                    });
                }

                return context.JoinAsync(DefaultCancellationToken);
            },
            new BraidOptions { Iterations = iterations, Seed = 12345, Timeout = TimeSpan.FromSeconds(2) },
            DefaultCancellationToken);

        Assert.Equal(iterations * workers, completed);
    }

    /// <summary>
    /// Verifies a scripted schedule can release several workers in reverse order.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncReplaysSeveralWorkersInScriptedOrder()
    {
        var gate = new object();
        var releases = new List<string>();
        var options = new BraidOptions
        {
            Iterations = 1,
            Seed = 12345,
            Timeout = TimeSpan.FromSeconds(2),
            Schedule = BraidSchedule.Replay(
                new BraidStep("worker-4", "ready"),
                new BraidStep("worker-3", "ready"),
                new BraidStep("worker-2", "ready"),
                new BraidStep("worker-1", "ready")),
        };

        await Braid.RunAsync(
            context =>
            {
                for (var index = 0; index < 4; index++)
                {
                    var worker = $"worker-{index + 1}";
                    context.Fork(async () =>
                    {
                        await BraidProbe.HitAsync("ready", DefaultCancellationToken);
                        lock (gate)
                        {
                            releases.Add(worker);
                        }
                    });
                }

                return context.JoinAsync(DefaultCancellationToken);
            },
            options,
            DefaultCancellationToken);

        Assert.Equal(["worker-4", "worker-3", "worker-2", "worker-1"], releases);
    }
}
