using System.Runtime.InteropServices;
using Xunit;

namespace Braid.Tests;

/// <summary>Covers replay arrival/hold/release scheduling behavior.</summary>
public sealed class BraidScheduleArriveReleaseTests : TestBase
{
    /// <summary>Verifies callback faults release held workers instead of deadlocking teardown.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task CallbackFaultWhileHeldDoesNotDeadlock()
    {
        var options = new BraidOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = BraidSchedule.Replay(BraidStep.Arrive("worker-1", "A"), BraidStep.Hit("worker-2", "fault"), BraidStep.Release("worker-1", "A")),
        };

        var runTask = BraidRunner.RunAsync(
            static async context =>
            {
                context.Fork(static async () => await BraidProbe.HitAsync("A", DefaultCancellationToken));

                context.Fork(static async () =>
                {
                    await BraidProbe.HitAsync("fault", DefaultCancellationToken);
                    throw new InvalidOperationException("callback boom");
                });

                await context.JoinAsync(DefaultCancellationToken);
            },
            options,
            DefaultCancellationToken);

        var completed = await Task.WhenAny(runTask, Task.Delay(TimeSpan.FromSeconds(5), TimeProvider.System, DefaultCancellationToken));

        Assert.Same(runTask, completed);
        var exception = Assert.IsType<BraidRunException>(runTask.Exception!.InnerException);
        var report = exception.ToString();
        Assert.Contains("A forked operation failed.", report, StringComparison.Ordinal);
        Assert.Contains("callback boom", report, StringComparison.Ordinal);
        Assert.Contains("Held workers:", report, StringComparison.Ordinal);
        Assert.Contains("worker-1", report, StringComparison.Ordinal);
        Assert.Contains("@ A", report, StringComparison.Ordinal);
    }

    /// <summary>Verifies external cancellation releases held workers instead of deadlocking teardown.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task CancellationWhileHeldDoesNotDeadlock()
    {
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;
        var cancelProbeReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = new BraidOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = BraidSchedule.Replay(BraidStep.Arrive("worker-1", "A"), BraidStep.Hit("worker-2", "cancel"), BraidStep.Release("worker-1", "A")),
        };

        var runTask = BraidRunner.RunAsync(
            async context =>
            {
                context.Fork(async () => await BraidProbe.HitAsync("A", cancellationToken));

                context.Fork(async () =>
                {
                    await BraidProbe.HitAsync("cancel", cancellationToken);
                    cancelProbeReleased.SetResult();
                });

                await context.JoinAsync(cancellationToken);
            },
            options,
            cancellationToken);

        var signal = await Task.WhenAny(cancelProbeReleased.Task, Task.Delay(TimeSpan.FromSeconds(5), TimeProvider.System, DefaultCancellationToken));
        Assert.Same(cancelProbeReleased.Task, signal);
        await cts.CancelAsync();
        var completed = await Task.WhenAny(runTask, Task.Delay(TimeSpan.FromSeconds(5), TimeProvider.System, DefaultCancellationToken));

        Assert.Same(runTask, completed);
        try
        {
            await runTask;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cancellation can propagate before the replay schedule reaches Release; both outcomes teardown without deadlock.
        }
    }

    /// <summary>Verifies a worker can be held at arrival while another worker runs.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ReplayArriveHoldsWorkerUntilRelease()
    {
        Lock sync = new();
        var observed = new List<string>();

        var options = new BraidOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = BraidSchedule.Replay(BraidStep.Arrive("worker-1", "before-write"), BraidStep.Hit("worker-2", "mutated"), BraidStep.Release("worker-1", "before-write")),
        };

        await BraidRunner.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    lock (sync)
                        observed.Add("worker-1-before-probe");

                    await BraidProbe.HitAsync("before-write", DefaultCancellationToken);

                    lock (sync)
                        observed.Add("worker-1-after-release");
                });

                context.Fork(async () =>
                {
                    await BraidProbe.HitAsync("mutated", DefaultCancellationToken);

                    lock (sync)
                        observed.Add("worker-2-mutated");
                });

                await context.JoinAsync(DefaultCancellationToken);
            },
            options,
            DefaultCancellationToken);

        lock (sync)
        {
            Assert.Equal(
                [
                    "worker-1-before-probe",
                    "worker-2-mutated",
                    "worker-1-after-release",
                ],
                observed);
        }
    }

    /// <summary>Verifies a held worker/probe cannot be arrived twice without release.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ReplayDuplicateArriveHeldFailsClearly()
    {
        var options = new BraidOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = BraidSchedule.Replay(BraidStep.Arrive("worker-1", "A"), BraidStep.Arrive("worker-1", "A")),
        };

        var exception = await Assertions.ExpectsAsync<BraidRunException>(
            BraidRunner.RunAsync(
                static async context =>
                {
                    context.Fork(static async () => await BraidProbe.HitAsync("A", DefaultCancellationToken));
                    await context.JoinAsync(DefaultCancellationToken);
                },
                options,
                DefaultCancellationToken));

        var report = exception.ToString();
        Assert.Contains("duplicate Arrive for held worker-1 at A", report, StringComparison.Ordinal);
        Assert.Contains("Held workers:", report, StringComparison.Ordinal);
        Assert.Contains("worker-1", report, StringComparison.Ordinal);
        Assert.Contains("@ A", report, StringComparison.Ordinal);
    }

    /// <summary>Verifies release cannot target a different probe than the held arrival.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ReplayReleaseDifferentProbeFailsClearly()
    {
        var options = new BraidOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = BraidSchedule.Replay(BraidStep.Arrive("worker-1", "A"), BraidStep.Release("worker-1", "B")),
        };

        var exception = await Assertions.ExpectsAsync<BraidRunException>(
            BraidRunner.RunAsync(
                static async context =>
                {
                    context.Fork(static async () => await BraidProbe.HitAsync("A", DefaultCancellationToken));
                    await context.JoinAsync(DefaultCancellationToken);
                },
                options,
                DefaultCancellationToken));

        var report = exception.ToString();
        Assert.Contains("release held worker-1 at B", report, StringComparison.Ordinal);
        Assert.Contains("actual probe is A", report, StringComparison.Ordinal);
        Assert.Contains("Release worker-1 @ B", report, StringComparison.Ordinal);
    }

    /// <summary>Verifies release cannot target a different worker than the held arrival.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ReplayReleaseDifferentWorkerFailsClearly()
    {
        var options = new BraidOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = BraidSchedule.Replay(BraidStep.Arrive("worker-1", "A"), BraidStep.Release("worker-2", "A")),
        };

        var exception = await Assertions.ExpectsAsync<BraidRunException>(
            BraidRunner.RunAsync(
                static async context =>
                {
                    context.Fork(static async () => await BraidProbe.HitAsync("A", DefaultCancellationToken));
                    context.Fork(static async () => await BraidProbe.HitAsync("A", DefaultCancellationToken));
                    await context.JoinAsync(DefaultCancellationToken);
                },
                options,
                DefaultCancellationToken));

        var report = exception.ToString();
        Assert.Contains("release held worker-2 at A", report, StringComparison.Ordinal);
        Assert.Contains("Release worker-2 @ A", report, StringComparison.Ordinal);
        Assert.Contains("Held workers:", report, StringComparison.Ordinal);
        Assert.Contains("worker-1", report, StringComparison.Ordinal);
        Assert.Contains("@ A", report, StringComparison.Ordinal);
    }

    /// <summary>Verifies release requires a previously held arrival for the same worker/probe.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ReplayReleaseWithoutArriveFailsClearly()
    {
        var options = new BraidOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = BraidSchedule.Replay(BraidStep.Release("worker-1", "A")),
        };

        var exception = await Assertions.ExpectsAsync<BraidRunException>(
            BraidRunner.RunAsync(
                static async context =>
                {
                    context.Fork(static async () => await BraidProbe.HitAsync("A", DefaultCancellationToken));
                    await context.JoinAsync(DefaultCancellationToken);
                },
                options,
                DefaultCancellationToken));

        var report = exception.ToString();
        Assert.Contains("release held worker-1 at A", report, StringComparison.Ordinal);
        Assert.Contains("Release worker-1 @ A", report, StringComparison.Ordinal);
    }

    /// <summary>Verifies schedules disambiguate workers even when probe names are the same.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncDistinguishesSameProbeWorkers()
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

        await BraidRunner.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    await BraidProbe.HitAsync("A", DefaultCancellationToken);
                    var idx = Interlocked.Increment(ref MemoryMarshal.GetArrayDataReference(releaseCursor)) - 1;
                    releaseOrder[idx] = 1;
                });

                context.Fork(async () =>
                {
                    await BraidProbe.HitAsync("A", DefaultCancellationToken);
                    var idx = Interlocked.Increment(ref MemoryMarshal.GetArrayDataReference(releaseCursor)) - 1;
                    releaseOrder[idx] = 2;
                });

                await context.JoinAsync(DefaultCancellationToken);
            },
            options,
            DefaultCancellationToken);

        Assert.Equal(2, Volatile.Read(ref MemoryMarshal.GetArrayDataReference(releaseCursor)));
        Assert.Equal(2, releaseOrder[0]);
        Assert.Equal(1, releaseOrder[1]);
    }

    /// <summary>Verifies later worker steps do not run before a required arrival step.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncDoesNotRunStepBeforeArrival()
    {
        var state = new int[3];
        var options = new BraidOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = BraidSchedule.Replay(BraidStep.Arrive("worker-1", "A"), BraidStep.Hit("worker-2", "B"), BraidStep.Release("worker-1", "A")),
        };

        await BraidRunner.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    _ = Interlocked.Exchange(ref MemoryMarshal.GetArrayDataReference(state), 1);
                    await BraidProbe.HitAsync("A", DefaultCancellationToken);
                    _ = Interlocked.Exchange(ref state[2], 1);
                });

                context.Fork(async () =>
                {
                    Assert.Equal(1, Volatile.Read(ref MemoryMarshal.GetArrayDataReference(state)));
                    Assert.Equal(0, Volatile.Read(ref state[2]));
                    await BraidProbe.HitAsync("B", DefaultCancellationToken);
                    _ = Interlocked.Exchange(ref state[1], 1);
                });

                await context.JoinAsync(DefaultCancellationToken);
            },
            options,
            DefaultCancellationToken);

        Assert.Equal(1, Volatile.Read(ref MemoryMarshal.GetArrayDataReference(state)));
        Assert.Equal(1, Volatile.Read(ref state[1]));
        Assert.Equal(1, Volatile.Read(ref state[2]));
    }

    /// <summary>Verifies unexpected probe hits are reported with expected and actual probes.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncFailsWhenUnexpectedProbe()
    {
        var options = new BraidOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = BraidSchedule.Replay(BraidStep.Arrive("worker-1", "A")),
        };

        var exception = await Assertions.ExpectsAsync<BraidRunException>(
            BraidRunner.RunAsync(
                static async context =>
                {
                    context.Fork(static async () => await BraidProbe.HitAsync("B", DefaultCancellationToken));
                    await context.JoinAsync(DefaultCancellationToken);
                },
                options,
                DefaultCancellationToken));

        var report = exception.ToString();
        Assert.Contains("arrive worker-1 at A", report, StringComparison.Ordinal);
        Assert.Contains("actual probe is B", report, StringComparison.Ordinal);
    }

    /// <summary>Verifies one worker can hit the same probe twice with deterministic replay steps.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncHandlesRepeatProbeDeterministic()
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

        await BraidRunner.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    await BraidProbe.HitAsync("A", DefaultCancellationToken);
                    _ = Interlocked.Increment(ref MemoryMarshal.GetArrayDataReference(hitsAfterRelease));

                    await BraidProbe.HitAsync("A", DefaultCancellationToken);
                    _ = Interlocked.Increment(ref MemoryMarshal.GetArrayDataReference(hitsAfterRelease));
                });

                await context.JoinAsync(DefaultCancellationToken);
            },
            options,
            DefaultCancellationToken);

        Assert.Equal(2, Volatile.Read(ref MemoryMarshal.GetArrayDataReference(hitsAfterRelease)));
    }

    /// <summary>Verifies hit steps keep legacy replay behavior and release matching workers.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncHitStepRetainsLegacyRelease()
    {
        var released = new List<string>();
        var options = new BraidOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = BraidSchedule.Replay(BraidStep.Hit("worker-2", "ready"), BraidStep.Hit("worker-1", "ready")),
        };

        await BraidRunner.RunAsync(
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

    /// <summary>Verifies wrong arrival order produces a clear replay diagnostic.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncReportsClearErrorWrongOrder()
    {
        var options = new BraidOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = BraidSchedule.Replay(BraidStep.Arrive("worker-1", "A"), BraidStep.Hit("worker-2", "B")),
        };

        var exception = await Assertions.ExpectsAsync<BraidRunException>(
            BraidRunner.RunAsync(
                static async context =>
                {
                    context.Fork(static async () => await BraidProbe.HitAsync("B", DefaultCancellationToken));
                    await context.JoinAsync(DefaultCancellationToken);
                },
                options,
                DefaultCancellationToken));

        Assert.Contains("could not be satisfied: arrive worker-1 at A", exception.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Verifies replay steps left after run completion are reported with step details.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task UnusedReplayStepsAreReported()
    {
        var options = new BraidOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = BraidSchedule.Replay(BraidStep.Hit("worker-1", "ready")),
        };

        var exception = await Assertions.ExpectsAsync<BraidRunException>(
            BraidRunner.RunAsync(static async context => await context.JoinAsync(DefaultCancellationToken), options, DefaultCancellationToken));

        var report = exception.ToString();
        Assert.Contains("unused steps", report, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Unused replay steps:", report, StringComparison.Ordinal);
        Assert.Contains("hit worker-1 ready", report, StringComparison.Ordinal);
    }
}
