namespace Braid.Tests;

/// <summary>Covers how nearby seeds spread the scheduling choices.</summary>
public sealed class SeedSpreadTests : TestBase
{
    /// <summary>Verifies consecutive small seeds do not all start the same worker first.</summary>
    /// <param name="workerCount">The number of workers.</param>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    [Arguments(3)]
    [Arguments(6)]
    public async Task SmallSeedsStartEveryWorkerFirst(int workerCount, CancellationToken cancellationToken)
    {
        var firstStarts = new int[workerCount];
        for (var seed = 1; seed <= 60; seed++)
        {
            var first = -1;
            await Runner.RunAsync(
                async context =>
                {
                    first = -1;
                    for (var worker = 0; worker < workerCount; worker++)
                    {
                        var index = worker;
                        context.Fork(
                            () =>
                            {
                                _ = Interlocked.CompareExchange(ref first, index, -1);
                                return Task.CompletedTask;
                            });
                    }

                    await context.JoinAsync(cancellationToken);
                },
                new RunOptions { Iterations = 1, Seed = seed },
                cancellationToken);
            firstStarts[first]++;
        }

        // An even spread gives each worker 60 / workerCount first starts. Half of that is well below what an unbiased choice gives.
        // When the first value of a small seed was a multiple of the seed, two of three workers got 6 and 7, and two of six workers got none.
        var fewest = int.MaxValue;
        for (var worker = 0; worker < workerCount; worker++)
            fewest = Math.Min(fewest, firstStarts[worker]);

        _ = await Assert.That(fewest).IsGreaterThanOrEqualTo(60 / workerCount / 2);
    }

    /// <summary>Verifies a hundred consecutive seeds, as a run with a hundred iterations uses, give most of the possible orders of two workers.</summary>
    /// <param name="baseSeed">The first of the consecutive seeds.</param>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    [Arguments(1)]
    [Arguments(1_000_000_000)]
    public async Task ConsecutiveSeedsGiveManyOrders(int baseSeed, CancellationToken cancellationToken)
    {
        var orders = new HashSet<string>(StringComparer.Ordinal);
        for (var seed = baseSeed; seed < baseSeed + 100; seed++)
        {
            var trace = string.Empty;
            await Runner.RunAsync(
                async context =>
                {
                    context.Fork(
                        "a",
                        async () =>
                        {
                            await Probe.HitAsync("a1", cancellationToken);
                            await Probe.HitAsync("a2", cancellationToken);
                        });
                    context.Fork(
                        "b",
                        async () =>
                        {
                            await Probe.HitAsync("b1", cancellationToken);
                            await Probe.HitAsync("b2", cancellationToken);
                        });
                    await context.JoinAsync(cancellationToken);
                    trace = string.Join('|', context.TraceSteps);
                },
                new RunOptions { Iterations = 1, Seed = seed },
                cancellationToken);
            _ = orders.Add(trace);
        }

        // Two workers with a start and two probes each have 20 orders. Seeding the generator with the seed itself gave 6 of them.
        _ = await Assert.That(orders.Count).IsGreaterThanOrEqualTo(15);
    }
}
