using System.Runtime.InteropServices;
using TUnit.Assertions.Enums;

namespace Braid.Tests;

/// <summary>Covers replay arrival/hold/release scheduling behavior.</summary>
public sealed class BraidScheduleArriveReleaseTests : TestBase
{
    /// <summary>Verifies callback faults release held workers instead of deadlocking teardown.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task CallbackFaultWhileHeldDoesNotDeadlock(CancellationToken cancellationToken)
    {
        var options = new RunOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = ReplaySchedule.Replay(ReplayStep.Arrive("worker-1", "A"), ReplayStep.Hit("worker-2", "fault"), ReplayStep.Release("worker-1", "A")),
        };

        var runTask = Runner.RunAsync(
            async context =>
            {
                context.Fork(async () => await Probe.HitAsync("A", cancellationToken));

                context.Fork(async () =>
                {
                    await Probe.HitAsync("fault", cancellationToken);
                    throw new InvalidOperationException("callback boom");
                });

                await context.JoinAsync(cancellationToken);
            },
            options,
            cancellationToken);

        var completed = await Task.WhenAny(runTask, Task.Delay(TimeSpan.FromSeconds(5), TimeProvider.System, cancellationToken));

        _ = await Assert.That(completed).IsSameReferenceAs(runTask);
        var aggregate = await Assert.That(runTask.Exception).IsNotNull();
        var exception = await Assert.That(aggregate.InnerException).IsTypeOf<RunException>();
        var report = exception!.ToString();
        _ = await Assert.That(report).Contains("A forked operation failed.");
        _ = await Assert.That(report).Contains("callback boom");
        _ = await Assert.That(report).Contains("Held workers:");
        _ = await Assert.That(report).Contains("worker-1");
        _ = await Assert.That(report).Contains("@ A");
    }

    /// <summary>Verifies external cancellation releases held workers instead of deadlocking teardown.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task CancellationWhileHeldDoesNotDeadlock(CancellationToken cancellationToken)
    {
        using var cts = new CancellationTokenSource();
        var externalToken = cts.Token;
        var cancelProbeReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = new RunOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = ReplaySchedule.Replay(ReplayStep.Arrive("worker-1", "A"), ReplayStep.Hit("worker-2", "cancel"), ReplayStep.Release("worker-1", "A")),
        };

        var runTask = Runner.RunAsync(
            async context =>
            {
                context.Fork(async () => await Probe.HitAsync("A", externalToken));

                context.Fork(async () =>
                {
                    await Probe.HitAsync("cancel", externalToken);
                    cancelProbeReleased.SetResult();
                });

                await context.JoinAsync(externalToken);
            },
            options,
            externalToken);

        var signal = await Task.WhenAny(cancelProbeReleased.Task, Task.Delay(TimeSpan.FromSeconds(5), TimeProvider.System, cancellationToken));
        _ = await Assert.That(signal).IsSameReferenceAs(cancelProbeReleased.Task);
        await cts.CancelAsync();
        var completed = await Task.WhenAny(runTask, Task.Delay(TimeSpan.FromSeconds(5), TimeProvider.System, cancellationToken));

        _ = await Assert.That(completed).IsSameReferenceAs(runTask);
        try
        {
            await runTask;
        }
        catch (OperationCanceledException) when (externalToken.IsCancellationRequested)
        {
            // Cancellation can propagate before the replay schedule reaches Release; both outcomes teardown without deadlock.
        }
    }

    /// <summary>Verifies a worker can be held at arrival while another worker runs.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ReplayArriveHoldsWorkerUntilRelease(CancellationToken cancellationToken)
    {
        Lock sync = new();
        var observed = new List<string>();

        var options = new RunOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = ReplaySchedule.Replay(ReplayStep.Arrive("worker-1", "before-write"), ReplayStep.Hit("worker-2", "mutated"), ReplayStep.Release("worker-1", "before-write")),
        };

        await Runner.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    lock (sync)
                        observed.Add("worker-1-before-probe");

                    await Probe.HitAsync("before-write", cancellationToken);

                    lock (sync)
                        observed.Add("worker-1-after-release");
                });

                context.Fork(async () =>
                {
                    await Probe.HitAsync("mutated", cancellationToken);

                    lock (sync)
                        observed.Add("worker-2-mutated");
                });

                await context.JoinAsync(cancellationToken);
            },
            options,
            cancellationToken);

        List<string> snapshot;
        lock (sync)
            snapshot = [.. observed];

        _ = await Assert.That(snapshot).IsEquivalentTo(
            [
                "worker-1-before-probe",
                "worker-2-mutated",
                "worker-1-after-release",
            ],
            CollectionOrdering.Matching);
    }

    /// <summary>Verifies a held worker/probe cannot be arrived twice without release.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ReplayDuplicateArriveHeldFailsClearly(CancellationToken cancellationToken)
    {
        var options = new RunOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = ReplaySchedule.Replay(ReplayStep.Arrive("worker-1", "A"), ReplayStep.Arrive("worker-1", "A")),
        };

        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                async context =>
                {
                    context.Fork(async () => await Probe.HitAsync("A", cancellationToken));
                    await context.JoinAsync(cancellationToken);
                },
                options,
                cancellationToken));

        var report = exception.ToString();
        _ = await Assert.That(report).Contains("duplicate Arrive for held worker-1 at A");
        _ = await Assert.That(report).Contains("Held workers:");
        _ = await Assert.That(report).Contains("worker-1");
        _ = await Assert.That(report).Contains("@ A");
    }

    /// <summary>Verifies release cannot target a different probe than the held arrival.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ReplayReleaseDifferentProbeFailsClearly(CancellationToken cancellationToken)
    {
        var options = new RunOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = ReplaySchedule.Replay(ReplayStep.Arrive("worker-1", "A"), ReplayStep.Release("worker-1", "B")),
        };

        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                async context =>
                {
                    context.Fork(async () => await Probe.HitAsync("A", cancellationToken));
                    await context.JoinAsync(cancellationToken);
                },
                options,
                cancellationToken));

        var report = exception.ToString();
        _ = await Assert.That(report).Contains("release held worker-1 at B");
        _ = await Assert.That(report).Contains("actual probe is A");
        _ = await Assert.That(report).Contains("Release worker-1 @ B");
    }

    /// <summary>Verifies release cannot target a different worker than the held arrival.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ReplayReleaseDifferentWorkerFailsClearly(CancellationToken cancellationToken)
    {
        var options = new RunOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = ReplaySchedule.Replay(ReplayStep.Arrive("worker-1", "A"), ReplayStep.Release("worker-2", "A")),
        };

        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                async context =>
                {
                    context.Fork(async () => await Probe.HitAsync("A", cancellationToken));
                    context.Fork(async () => await Probe.HitAsync("A", cancellationToken));
                    await context.JoinAsync(cancellationToken);
                },
                options,
                cancellationToken));

        var report = exception.ToString();
        _ = await Assert.That(report).Contains("release held worker-2 at A");
        _ = await Assert.That(report).Contains("Release worker-2 @ A");
        _ = await Assert.That(report).Contains("Held workers:");
        _ = await Assert.That(report).Contains("worker-1");
        _ = await Assert.That(report).Contains("@ A");
    }

    /// <summary>Verifies release requires a previously held arrival for the same worker/probe.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ReplayReleaseWithoutArriveFailsClearly(CancellationToken cancellationToken)
    {
        var options = new RunOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = ReplaySchedule.Replay(ReplayStep.Release("worker-1", "A")),
        };

        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                async context =>
                {
                    context.Fork(async () => await Probe.HitAsync("A", cancellationToken));
                    await context.JoinAsync(cancellationToken);
                },
                options,
                cancellationToken));

        var report = exception.ToString();
        _ = await Assert.That(report).Contains("release held worker-1 at A");
        _ = await Assert.That(report).Contains("Release worker-1 @ A");
    }

    /// <summary>Verifies schedules disambiguate workers even when probe names are the same.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task RunAsyncDistinguishesSameProbeWorkers(CancellationToken cancellationToken)
    {
        var releaseOrder = new int[2];
        var releaseCursor = new int[1];
        var options = new RunOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = ReplaySchedule.Replay(
                ReplayStep.Arrive("worker-1", "A"),
                ReplayStep.Arrive("worker-2", "A"),
                ReplayStep.Release("worker-2", "A"),
                ReplayStep.Release("worker-1", "A")),
        };

        await Runner.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    await Probe.HitAsync("A", cancellationToken);
                    var idx = Interlocked.Increment(ref MemoryMarshal.GetArrayDataReference(releaseCursor)) - 1;
                    releaseOrder[idx] = 1;
                });

                context.Fork(async () =>
                {
                    await Probe.HitAsync("A", cancellationToken);
                    var idx = Interlocked.Increment(ref MemoryMarshal.GetArrayDataReference(releaseCursor)) - 1;
                    releaseOrder[idx] = 2;
                });

                await context.JoinAsync(cancellationToken);
            },
            options,
            cancellationToken);

        _ = await Assert.That(Volatile.Read(ref MemoryMarshal.GetArrayDataReference(releaseCursor))).IsEqualTo(2);
        _ = await Assert.That(releaseOrder[0]).IsEqualTo(2);
        _ = await Assert.That(releaseOrder[1]).IsEqualTo(1);
    }

    /// <summary>Verifies later worker steps do not run before a required arrival step.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task RunAsyncDoesNotRunStepBeforeArrival(CancellationToken cancellationToken)
    {
        var state = new int[3];
        var options = new RunOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = ReplaySchedule.Replay(ReplayStep.Arrive("worker-1", "A"), ReplayStep.Hit("worker-2", "B"), ReplayStep.Release("worker-1", "A")),
        };

        await Runner.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    _ = Interlocked.Exchange(ref MemoryMarshal.GetArrayDataReference(state), 1);
                    await Probe.HitAsync("A", cancellationToken);
                    _ = Interlocked.Exchange(ref state[2], 1);
                });

                context.Fork(async () =>
                {
                    _ = await Assert.That(Volatile.Read(ref MemoryMarshal.GetArrayDataReference(state))).IsEqualTo(1);
                    _ = await Assert.That(Volatile.Read(ref state[2])).IsEqualTo(0);
                    await Probe.HitAsync("B", cancellationToken);
                    _ = Interlocked.Exchange(ref state[1], 1);
                });

                await context.JoinAsync(cancellationToken);
            },
            options,
            cancellationToken);

        _ = await Assert.That(Volatile.Read(ref MemoryMarshal.GetArrayDataReference(state))).IsEqualTo(1);
        _ = await Assert.That(Volatile.Read(ref state[1])).IsEqualTo(1);
        _ = await Assert.That(Volatile.Read(ref state[2])).IsEqualTo(1);
    }

    /// <summary>Verifies unexpected probe hits are reported with expected and actual probes.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task RunAsyncFailsWhenUnexpectedProbe(CancellationToken cancellationToken)
    {
        var options = new RunOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = ReplaySchedule.Replay(ReplayStep.Arrive("worker-1", "A")),
        };

        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                async context =>
                {
                    context.Fork(async () => await Probe.HitAsync("B", cancellationToken));
                    await context.JoinAsync(cancellationToken);
                },
                options,
                cancellationToken));

        var report = exception.ToString();
        _ = await Assert.That(report).Contains("arrive worker-1 at A");
        _ = await Assert.That(report).Contains("actual probe is B");
    }

    /// <summary>Verifies one worker can hit the same probe twice with deterministic replay steps.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task RunAsyncHandlesRepeatProbeDeterministic(CancellationToken cancellationToken)
    {
        var hitsAfterRelease = new int[1];

        var options = new RunOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = ReplaySchedule.Replay(
                ReplayStep.Arrive("worker-1", "A"),
                ReplayStep.Release("worker-1", "A"),
                ReplayStep.Arrive("worker-1", "A"),
                ReplayStep.Release("worker-1", "A")),
        };

        await Runner.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    await Probe.HitAsync("A", cancellationToken);
                    _ = Interlocked.Increment(ref MemoryMarshal.GetArrayDataReference(hitsAfterRelease));

                    await Probe.HitAsync("A", cancellationToken);
                    _ = Interlocked.Increment(ref MemoryMarshal.GetArrayDataReference(hitsAfterRelease));
                });

                await context.JoinAsync(cancellationToken);
            },
            options,
            cancellationToken);

        _ = await Assert.That(Volatile.Read(ref MemoryMarshal.GetArrayDataReference(hitsAfterRelease))).IsEqualTo(2);
    }

    /// <summary>Verifies hit steps keep legacy replay behavior and release matching workers.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task RunAsyncHitStepRetainsLegacyRelease(CancellationToken cancellationToken)
    {
        var released = new List<string>();
        var options = new RunOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = ReplaySchedule.Replay(ReplayStep.Hit("worker-2", "ready"), ReplayStep.Hit("worker-1", "ready")),
        };

        await Runner.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    await Probe.HitAsync("ready", cancellationToken);
                    released.Add("worker-1");
                });

                context.Fork(async () =>
                {
                    await Probe.HitAsync("ready", cancellationToken);
                    released.Add("worker-2");
                });

                await context.JoinAsync(cancellationToken);
            },
            options,
            cancellationToken);

        _ = await Assert.That(released).IsEquivalentTo(["worker-2", "worker-1"], CollectionOrdering.Matching);
    }

    /// <summary>Verifies wrong arrival order produces a clear replay diagnostic.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task RunAsyncReportsClearErrorWrongOrder(CancellationToken cancellationToken)
    {
        var options = new RunOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = ReplaySchedule.Replay(ReplayStep.Arrive("worker-1", "A"), ReplayStep.Hit("worker-2", "B")),
        };

        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                async context =>
                {
                    context.Fork(async () => await Probe.HitAsync("B", cancellationToken));
                    await context.JoinAsync(cancellationToken);
                },
                options,
                cancellationToken));

        _ = await Assert.That(exception.ToString()).Contains("could not be satisfied: arrive worker-1 at A");
    }

    /// <summary>Verifies replay steps left after run completion are reported with step details.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task UnusedReplayStepsAreReported(CancellationToken cancellationToken)
    {
        var options = new RunOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = ReplaySchedule.Replay(ReplayStep.Hit("worker-1", "ready")),
        };

        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(async context => await context.JoinAsync(cancellationToken), options, cancellationToken));

        var report = exception.ToString();
        _ = await Assert.That(report).Contains("unused steps", StringComparison.OrdinalIgnoreCase);
        _ = await Assert.That(report).Contains("Unused replay steps:");
        _ = await Assert.That(report).Contains("hit worker-1 ready");
    }
}
