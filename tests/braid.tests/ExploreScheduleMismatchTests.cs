namespace Braid.Tests;

/// <summary>Covers generated schedules that do not fit the real run, which exploration skips.</summary>
public sealed class ExploreScheduleMismatchTests : TestBase
{
    /// <summary>Verifies exploration passes when generated schedules ask for probes that the run does not reach.</summary>
    /// <param name="seed">The exploration seed, which decides the order the discovery run takes.</param>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task MismatchedScheduleIsSkipped(int seed, CancellationToken cancellationToken)
    {
        // Each worker hits its second probe only if the other worker has finished. Discovery runs one worker after the other, so the second one
        // hits its second probe; a generated schedule that runs that worker first expects a probe the worker then skips.
        await Runner.ExploreAsync(
            options => options.WithSeed(seed).WithMaxSchedules(10),
            async braid =>
            {
                var w1Done = 0;
                var w2Done = 0;
                await braid.WorkerAsync("w1", () => RunWorkerAsync("a", "b", () => Volatile.Read(ref w2Done) != 0, () => Volatile.Write(ref w1Done, 1)));
                await braid.WorkerAsync("w2", () => RunWorkerAsync("x", "y", () => Volatile.Read(ref w1Done) != 0, () => Volatile.Write(ref w2Done, 1)));
                await braid.JoinAsync(cancellationToken);
            },
            cancellationToken);

        async Task RunWorkerAsync(string first, string second, Func<bool> hitsSecond, Action done)
        {
            await Probe.HitAsync(first, cancellationToken);
            if (hitsSecond())
                await Probe.HitAsync(second, cancellationToken);

            done();
        }
    }
}
