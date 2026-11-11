using System;
using System.Threading;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace Braid.Tests;

/// <summary>Covers collecting distinct failures during exploration.</summary>
public sealed class ExploreFailureCollectionTests : TestBase
{
    private static readonly ExploreOptions Bounds = new(20, 2, TimeSpan.FromSeconds(5));

    /// <summary>Verifies exploration continues after failure and returns distinct failures in order.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task CollectsDistinctFailuresInOrder(CancellationToken cancellationToken)
    {
        var failures = await Runner.ExploreAsync(
            in Bounds, context => FailByOrderAsync(context, cancellationToken), 3, cancellationToken);

        _ = await Assert.That(failures.Count).IsEqualTo(2);
        _ = await Assert.That(failures[0].InnerException!.Message).IsEqualTo("left");
        _ = await Assert.That(failures[1].InnerException!.Message).IsEqualTo("right");
    }

    /// <summary>Verifies reaching the failure limit prevents additional callbacks.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task StopsAtFailureLimit(CancellationToken cancellationToken)
    {
        var invocations = new CompletionCounter();
        var failures = await Runner.ExploreAsync(
            in Bounds,
            context =>
            {
                _ = invocations.Increment();
                return FailByOrderAsync(context, cancellationToken);
            },
            1,
            cancellationToken);

        var failure = await Assert.That(failures).HasSingleItem();
        _ = await Assert.That(failure.InnerException!.Message).IsEqualTo("left");
        _ = await Assert.That(invocations.Value).IsEqualTo(1);
    }

    /// <summary>Verifies the collector rejects a callback that fails before a known replay prefix.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task RejectsFailureBeforeKnownPrefix(CancellationToken cancellationToken)
    {
        var invocations = new CompletionCounter();
        var failure = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.ExploreAsync(
                in Bounds,
                context => invocations.Increment() == 1
                    ? FailByOrderAsync(context, cancellationToken)
                    : Task.FromException(new InvalidOperationException("changed before replay")),
                3,
                cancellationToken));

        _ = await Assert.That(failure.Message).Contains("The test did not repeat under the same schedule");
        _ = await Assert.That(failure.InnerException!.InnerException!.Message).IsEqualTo("changed before replay");
        _ = await Assert.That(invocations.Value).IsEqualTo(2);
    }

    private static async Task FailByOrderAsync(ExploreContext context, CancellationToken cancellationToken)
    {
        string? first = null;
        await context.WorkerAsync("left", () => HitAndRecordAsync("left"));
        await context.WorkerAsync("right", () => HitAndRecordAsync("right"));
        await context.JoinAsync(cancellationToken);
        throw new InvalidOperationException(first);

        async Task HitAndRecordAsync(string worker)
        {
            await Probe.HitAsync("ready", cancellationToken);
            first ??= worker;
        }
    }
}
