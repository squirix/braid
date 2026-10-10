using TUnit.Assertions.Enums;

namespace Braid.Tests;

/// <summary>Covers start steps in typed schedules and in replay text.</summary>
public sealed class StartStepTextTests : TestBase
{
    /// <summary>Verifies replay text with start lines parses to start steps, whatever the case of the operation.</summary>
    [Test]
    public async Task ParseReadsStartSteps()
    {
        var schedule = ReplaySchedule.Parse("start w2\nSTART w1\nhit w1 a");

        _ = await Assert.That(schedule.Steps).IsEquivalentTo([ReplayStep.Start("w2"), ReplayStep.Start("w1"), ReplayStep.Hit("w1", "a")], CollectionOrdering.Matching);
    }

    /// <summary>Verifies start steps are written as "start" lines that parse back to the same schedule.</summary>
    [Test]
    public async Task ToReplayTextWritesStartSteps()
    {
        var schedule = ReplaySchedule.Replay(ReplayStep.Start("w2"), ReplayStep.Hit("w2", "b"));

        var text = schedule.ToReplayText();

        _ = await Assert.That(text).IsEqualTo("start w2" + Environment.NewLine + "hit w2 b");
        _ = await Assert.That(ReplaySchedule.Parse(text).Steps).IsEquivalentTo(schedule.Steps, CollectionOrdering.Matching);
    }

    /// <summary>Verifies a start line with a probe name is rejected: a start has no probe.</summary>
    [Test]
    public async Task ParseRejectsStartWithProbe()
    {
        var exception = BraidAssertions.AssertExpects<FormatException>(static () => ReplaySchedule.Parse("start w2 a"));

        _ = await Assert.That(exception.Message).IsEqualTo("Line 1: Expected exactly 2 tokens (start, worker id); found 3.");
    }

    /// <summary>Verifies a start line without a worker id is rejected.</summary>
    [Test]
    public async Task ParseRejectsStartWithoutWorker()
    {
        var exception = BraidAssertions.AssertExpects<FormatException>(static () => ReplaySchedule.Parse("start"));

        _ = await Assert.That(exception.Message).IsEqualTo("Line 1: Expected exactly 2 tokens (start, worker id); found 1.");
    }

    /// <summary>Verifies an unknown operation names "start" among the expected ones.</summary>
    [Test]
    public async Task UnknownOperationNamesStart()
    {
        var exception = BraidAssertions.AssertExpects<FormatException>(static () => ReplaySchedule.Parse("begin w2"));

        _ = await Assert.That(exception.Message).IsEqualTo("Line 1: Unknown operation 'begin'. Expected 'hit', 'arrive', 'release', or 'start'.");
    }

    /// <summary>Verifies the start step factory creates a step of the start kind for the worker.</summary>
    [Test]
    public async Task StartCreatesAStartStep()
    {
        var step = ReplayStep.Start("w1");

        _ = await Assert.That(step.Kind).IsEqualTo(ReplayStepKind.Start);
        _ = await Assert.That(step.WorkerId).IsEqualTo("w1");
    }

    /// <summary>Verifies a start step built with a probe name is rejected by a schedule.</summary>
    [Test]
    public async Task StartStepWithProbeIsRejected()
    {
        var exception = BraidAssertions.AssertExpects<ArgumentException>(static () => _ = ReplaySchedule.Replay(new ReplayStep("w1", "a", ReplayStepKind.Start)));

        _ = await Assert.That(exception.Message).StartsWith("A start step has no probe.");
    }
}
