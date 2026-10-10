namespace Braid.Tests;

/// <summary>Covers <see cref="RunContext.TraceSteps" /> read inside the run callback and after the run.</summary>
public sealed class TraceStepsTests : TestBase
{
    /// <summary>Verifies the trace grows as the callback forks and joins, and a snapshot taken earlier does not change.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task TraceIsLiveInsideTheCallback(CancellationToken cancellationToken)
    {
        IReadOnlyList<string> beforeFork = ["unset"];
        IReadOnlyList<string> afterFork = [];
        IReadOnlyList<string> afterJoin = [];

        await Runner.RunAsync(
            async context =>
            {
                beforeFork = context.TraceSteps;
                context.Fork("w", async () => await Probe.HitAsync("a", cancellationToken));
                afterFork = context.TraceSteps;
                await context.JoinAsync(cancellationToken);
                afterJoin = context.TraceSteps;
            },
            new RunOptions { Iterations = 1, Seed = 1 },
            cancellationToken);

        _ = await Assert.That(beforeFork).IsEmpty();
        _ = await Assert.That(string.Join(" | ", afterFork)).IsEqualTo("w forked");
        _ = await Assert.That(string.Join(" | ", afterJoin)).IsEqualTo("w forked | w released | w hit a | w released at a | w completed");
    }

    /// <summary>Verifies the trace read after the run is the whole trace of that run.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task TraceStaysReadableAfterTheRun(CancellationToken cancellationToken)
    {
        RunContext? captured = null;
        IReadOnlyList<string> afterJoin = [];

        await Runner.RunAsync(
            async context =>
            {
                captured = context;
                context.Fork("w", async () => await Probe.HitAsync("a", cancellationToken));
                await context.JoinAsync(cancellationToken);
                afterJoin = context.TraceSteps;
            },
            new RunOptions { Iterations = 1, Seed = 1 },
            cancellationToken);

        _ = await Assert.That(captured).IsNotNull();
        _ = await Assert.That(string.Join(" | ", captured.TraceSteps)).IsEqualTo(string.Join(" | ", afterJoin));
        _ = await Assert.That(afterJoin.Count).IsEqualTo(5);
        _ = await Assert.That(captured.TraceSteps).IsSameReferenceAs(captured.TraceSteps);
    }

    /// <summary>Verifies each iteration has its own trace, so the trace inside the callback never includes earlier iterations.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task EachIterationStartsWithAnEmptyTrace(CancellationToken cancellationToken)
    {
        var stepsAtStart = new List<int>();
        var stepsAtEnd = new List<int>();

        await Runner.RunAsync(
            async context =>
            {
                stepsAtStart.Add(context.TraceSteps.Count);
                context.Fork("w", async () => await Probe.HitAsync("a", cancellationToken));
                await context.JoinAsync(cancellationToken);
                stepsAtEnd.Add(context.TraceSteps.Count);
            },
            new RunOptions { Iterations = 3, Seed = 1 },
            cancellationToken);

        _ = await Assert.That(stepsAtStart).IsEquivalentTo([0, 0, 0]);
        _ = await Assert.That(stepsAtEnd).IsEquivalentTo([5, 5, 5]);
    }
}
