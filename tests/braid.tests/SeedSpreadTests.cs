namespace Braid.Tests;

/// <summary>Covers how small seeds spread the first scheduling choice.</summary>
public sealed class SeedSpreadTests : TestBase
{
    /// <summary>Verifies consecutive small seeds do not all start the same worker first.</summary>
    /// <param name="workerCount">The number of workers.</param>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
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

        // An even spread gives each worker 60 / workerCount first starts. Half of that is well below what an unbiased choice gives,
        // and above the 6 and 7 that two of three workers got when the first value of a small seed was a multiple of the seed.
        var fewest = int.MaxValue;
        for (var worker = 0; worker < workerCount; worker++)
            fewest = Math.Min(fewest, firstStarts[worker]);

        _ = await Assert.That(fewest).IsGreaterThanOrEqualTo(60 / workerCount / 2);
    }
}
