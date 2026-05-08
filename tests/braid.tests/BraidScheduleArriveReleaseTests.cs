using Xunit;

namespace Braid.Tests;

/// <summary>
/// Covers replay arrival/hold/release scheduling behavior.
/// </summary>
public sealed class BraidScheduleArriveReleaseTests : TestBase
{
    /// <summary>
    /// Verifies hit steps keep legacy replay behavior and release matching workers.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncHitStepRetainsLegacyReleaseBehavior()
    {
        var released = new List<string>();
        var options = new BraidOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = BraidSchedule.Replay(BraidStep.Hit("worker-2", "ready"), BraidStep.Hit("worker-1", "ready")),
        };

        await Braid.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    await BraidProbe.HitAsync("ready", DefaultCancellationToken);
                    released.Add("worker-1");
                });

                context.Fork(async () =>
                {
                    await BraidProbe.HitAsync("ready", DefaultCancellationToken);
                    released.Add("worker-2");
                });

                await context.JoinAsync(DefaultCancellationToken);
            },
            options,
            DefaultCancellationToken);

        Assert.Equal(["worker-2", "worker-1"], released);
    }

    /// <summary>
    /// Verifies a worker can be held at arrival while another worker runs.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncCanHoldArrivalUntilAnotherWorkerStepRuns()
    {
        var state = new int[2];

        var options = new BraidOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = BraidSchedule.Replay(
                BraidStep.Arrive("worker-1", "A"),
                BraidStep.Hit("worker-2", "B"),
                BraidStep.Release("worker-1", "A")),
        };

        await Braid.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    _ = Interlocked.Exchange(ref state[0], 1);
                    await BraidProbe.HitAsync("A", DefaultCancellationToken);
                    _ = Interlocked.Exchange(ref state[1], 1);
                });

                context.Fork(async () =>
                {
                    Assert.Equal(1, Volatile.Read(ref state[0]));
                    Assert.Equal(0, Volatile.Read(ref state[1]));
                    await BraidProbe.HitAsync("B", DefaultCancellationToken);
                });

                await context.JoinAsync(DefaultCancellationToken);
            },
            options,
            DefaultCancellationToken);

        Assert.Equal(1, Volatile.Read(ref state[1]));
    }

    /// <summary>
    /// Verifies later worker steps do not run before a required arrival step.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncDoesNotRunLaterWorkerStepBeforeRequiredArrival()
    {
        var state = new int[3];
        var options = new BraidOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = BraidSchedule.Replay(BraidStep.Arrive("worker-1", "A"), BraidStep.Hit("worker-2", "B"), BraidStep.Release("worker-1", "A")),
        };

        await Braid.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    _ = Interlocked.Exchange(ref state[0], 1);
                    await BraidProbe.HitAsync("A", DefaultCancellationToken);
                    _ = Interlocked.Exchange(ref state[2], 1);
                });

                context.Fork(async () =>
                {
                    Assert.Equal(1, Volatile.Read(ref state[0]));
                    Assert.Equal(0, Volatile.Read(ref state[2]));
                    await BraidProbe.HitAsync("B", DefaultCancellationToken);
                    _ = Interlocked.Exchange(ref state[1], 1);
                });

                await context.JoinAsync(DefaultCancellationToken);
            },
            options,
            DefaultCancellationToken);

        Assert.Equal(1, Volatile.Read(ref state[0]));
        Assert.Equal(1, Volatile.Read(ref state[1]));
        Assert.Equal(1, Volatile.Read(ref state[2]));
    }

    /// <summary>
    /// Verifies wrong arrival order produces a clear replay diagnostic.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncReportsClearErrorWhenOrderIsWrongForArrival()
    {
        var options = new BraidOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = BraidSchedule.Replay(BraidStep.Arrive("worker-1", "A"), BraidStep.Hit("worker-2", "B")),
        };

        var exception = await Assert.ThrowsAsync<BraidRunException>(async () =>
        {
            await Braid.RunAsync(
                static async context =>
                {
                    context.Fork(static async () => { await BraidProbe.HitAsync("B", DefaultCancellationToken); });
                    await context.JoinAsync(DefaultCancellationToken);
                },
                options,
                DefaultCancellationToken);
        });

        Assert.Contains("could not be satisfied: arrive worker-1 at A", exception.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies release requires a previously held arrival for the same worker/probe.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncFailsClearlyWhenReleaseHasNoArrivedWorker()
    {
        var options = new BraidOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = BraidSchedule.Replay(BraidStep.Release("worker-1", "A")),
        };

        var exception = await Assert.ThrowsAsync<BraidRunException>(async () =>
        {
            await Braid.RunAsync(
                static async context =>
                {
                    context.Fork(static async () => { await BraidProbe.HitAsync("A", DefaultCancellationToken); });
                    await context.JoinAsync(DefaultCancellationToken);
                },
                options,
                DefaultCancellationToken);
        });

        Assert.Contains("could not be satisfied: release held worker-1 at A", exception.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies schedules disambiguate workers even when probe names are the same.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncDistinguishesSameProbeAcrossWorkers()
    {
        var releaseOrder = new int[2];
        var releaseCursor = new int[1];
        var options = new BraidOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = BraidSchedule.Replay(
                BraidStep.Arrive("worker-1", "A"),
                BraidStep.Arrive("worker-2", "A"),
                BraidStep.Release("worker-2", "A"),
                BraidStep.Release("worker-1", "A")),
        };

        await Braid.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    await BraidProbe.HitAsync("A", DefaultCancellationToken);
                    var idx = Interlocked.Increment(ref releaseCursor[0]) - 1;
                    releaseOrder[idx] = 1;
                });

                context.Fork(async () =>
                {
                    await BraidProbe.HitAsync("A", DefaultCancellationToken);
                    var idx = Interlocked.Increment(ref releaseCursor[0]) - 1;
                    releaseOrder[idx] = 2;
                });

                await context.JoinAsync(DefaultCancellationToken);
            },
            options,
            DefaultCancellationToken);

        Assert.Equal(2, Volatile.Read(ref releaseCursor[0]));
        Assert.Equal(2, releaseOrder[0]);
        Assert.Equal(1, releaseOrder[1]);
    }

    /// <summary>
    /// Verifies unexpected probe hits are reported with expected and actual probes.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncFailsClearlyWhenWorkerHitsUnexpectedProbe()
    {
        var options = new BraidOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = BraidSchedule.Replay(BraidStep.Arrive("worker-1", "A")),
        };

        var exception = await Assert.ThrowsAsync<BraidRunException>(async () =>
        {
            await Braid.RunAsync(
                static async context =>
                {
                    context.Fork(static async () => { await BraidProbe.HitAsync("B", DefaultCancellationToken); });
                    await context.JoinAsync(DefaultCancellationToken);
                },
                options,
                DefaultCancellationToken);
        });

        var report = exception.ToString();
        Assert.Contains("arrive worker-1 at A", report, StringComparison.Ordinal);
        Assert.Contains("actual probe is B", report, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies one worker can hit the same probe twice with deterministic replay steps.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncHandlesRepeatedSameProbeBySameWorkerDeterministically()
    {
        var hitsAfterRelease = new int[1];

        var options = new BraidOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = BraidSchedule.Replay(
                BraidStep.Arrive("worker-1", "A"),
                BraidStep.Release("worker-1", "A"),
                BraidStep.Arrive("worker-1", "A"),
                BraidStep.Release("worker-1", "A")),
        };

        await Braid.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    await BraidProbe.HitAsync("A", DefaultCancellationToken);
                    _ = Interlocked.Increment(ref hitsAfterRelease[0]);

                    await BraidProbe.HitAsync("A", DefaultCancellationToken);
                    _ = Interlocked.Increment(ref hitsAfterRelease[0]);
                });

                await context.JoinAsync(DefaultCancellationToken);
            },
            options,
            DefaultCancellationToken);

        Assert.Equal(2, Volatile.Read(ref hitsAfterRelease[0]));
    }
}
