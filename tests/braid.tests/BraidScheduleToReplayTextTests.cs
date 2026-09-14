namespace Braid.Tests;

/// <summary>Covers canonical replay text export from <see cref="ReplaySchedule" />.</summary>
public sealed class BraidScheduleToReplayTextTests : TestBase
{
    /// <summary>Verifies exported text has no trailing whitespace.</summary>
    [Test]
    public async Task ToReplayTextDoesNotAddTrailingWhitespace()
    {
        var schedule = ReplaySchedule.Replay(ReplayStep.Hit("w1", "p1"), ReplayStep.Hit("w2", "p2"));

        var text = schedule.ToReplayText();

        _ = await Assert.That(char.IsWhiteSpace(text[^1])).IsFalse();
        _ = await Assert.That(text).DoesNotContain(" \n");
        _ = await Assert.That(text).DoesNotContain(" \r");
    }

    /// <summary>Verifies an empty typed schedule exports to an empty string.</summary>
    [Test]
    public async Task ToReplayTextEmptyStringForEmptySchedule()
    {
        var schedule = ReplaySchedule.Replay();

        _ = await Assert.That(schedule.ToReplayText()).IsEqualTo(string.Empty);
    }

    /// <summary>Verifies probe name casing is preserved in export and round-trip.</summary>
    [Test]
    public async Task ToReplayTextPreservesProbeCase()
    {
        var original = ReplaySchedule.Replay(ReplayStep.Hit("w", "Cache-Hit"));

        var text = original.ToReplayText();

        _ = await Assert.That(text).IsEqualTo("hit w Cache-Hit");

        var parsed = ReplaySchedule.Parse(text);
        var probeStep = await Assert.That(parsed.Steps).HasSingleItem();
        _ = await Assert.That(probeStep.ProbeName).IsEqualTo("Cache-Hit");
    }

    /// <summary>Verifies worker id casing is preserved in export and round-trip.</summary>
    [Test]
    public async Task ToReplayTextPreservesWorkerCase()
    {
        var original = ReplaySchedule.Replay(ReplayStep.Hit("Worker-1", "x"));

        var text = original.ToReplayText();

        _ = await Assert.That(text).IsEqualTo("hit Worker-1 x");

        var parsed = ReplaySchedule.Parse(text);
        var workerStep = await Assert.That(parsed.Steps).HasSingleItem();
        _ = await Assert.That(workerStep.WorkerId).IsEqualTo("Worker-1");
    }

    /// <summary>Verifies probe names containing whitespace cannot be exported.</summary>
    [Test]
    public async Task ToReplayTextRejectsProbeWithWhitespace()
    {
        var schedule = ReplaySchedule.Replay(ReplayStep.Hit("w", "probe name"));

        var ex = BraidAssertions.AssertExpects<InvalidOperationException, ReplaySchedule>(schedule, static state => _ = state.ToReplayText());

        _ = await Assert.That(ex.Message).Contains("probe", StringComparison.OrdinalIgnoreCase);
        _ = await Assert.That(ex.Message).Contains("whitespace", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies worker ids containing whitespace cannot be exported.</summary>
    [Test]
    public async Task ToReplayTextRejectsWorkerWithWhitespace()
    {
        var schedule = ReplaySchedule.Replay(ReplayStep.Hit("worker 1", "p"));

        var ex = BraidAssertions.AssertExpects<InvalidOperationException, ReplaySchedule>(schedule, static state => _ = state.ToReplayText());

        _ = await Assert.That(ex.Message).Contains("worker", StringComparison.OrdinalIgnoreCase);
        _ = await Assert.That(ex.Message).Contains("whitespace", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies an arrive step exports as an arrive line.</summary>
    [Test]
    public async Task ToReplayTextReturnsArriveStep()
    {
        var schedule = ReplaySchedule.Replay(ReplayStep.Arrive("worker-1", "cache-hit"));

        _ = await Assert.That(schedule.ToReplayText()).IsEqualTo("arrive worker-1 cache-hit");
    }

    /// <summary>Verifies a hit step exports as a hit line.</summary>
    [Test]
    public async Task ToReplayTextReturnsHitStep()
    {
        var schedule = ReplaySchedule.Replay(ReplayStep.Hit("worker-1", "after-read"));

        _ = await Assert.That(schedule.ToReplayText()).IsEqualTo("hit worker-1 after-read");
    }

    /// <summary>Verifies multiple steps export in order with platform newlines.</summary>
    [Test]
    public async Task ToReplayTextReturnsMultipleStepsInOrder()
    {
        var schedule = ReplaySchedule.Replay(ReplayStep.Hit("worker-1", "after-read"), ReplayStep.Arrive("worker-2", "before-write"), ReplayStep.Release("worker-2", "before-write"));

        _ = await Assert.That(schedule.ToReplayText()).IsEqualTo("hit worker-1 after-read" + Environment.NewLine + "arrive worker-2 before-write" + Environment.NewLine + "release worker-2 before-write");
    }

    /// <summary>Verifies a release step exports as a release line.</summary>
    [Test]
    public async Task ToReplayTextReturnsReleaseStep()
    {
        var schedule = ReplaySchedule.Replay(ReplayStep.Release("worker-1", "cache-hit"));

        _ = await Assert.That(schedule.ToReplayText()).IsEqualTo("release worker-1 cache-hit");
    }

    /// <summary>Verifies arrive and release schedules round-trip through text.</summary>
    [Test]
    public Task ToReplayTextRoundTripsArriveRelease()
    {
        var original = ReplaySchedule.Replay(ReplayStep.Arrive("worker-1", "A"), ReplayStep.Hit("worker-2", "B"), ReplayStep.Release("worker-1", "A"));

        var parsed = ReplaySchedule.Parse(original.ToReplayText());

        return AssertSchedulesEqualAsync(original, parsed);
    }

    /// <summary>Verifies hit schedules round-trip through text.</summary>
    [Test]
    public Task ToReplayTextRoundTripsHit()
    {
        var original = ReplaySchedule.Replay(ReplayStep.Hit("worker-1", "after-read"));

        var parsed = ReplaySchedule.Parse(original.ToReplayText());

        return AssertSchedulesEqualAsync(original, parsed);
    }

    /// <summary>Verifies multi-step schedules round-trip through text.</summary>
    [Test]
    public Task ToReplayTextRoundTripsMultipleSteps()
    {
        var original = ReplaySchedule.Replay(ReplayStep.Hit("worker-1", "after-read"), ReplayStep.Arrive("worker-2", "before-write"), ReplayStep.Release("worker-2", "before-write"));

        var parsed = ReplaySchedule.Parse(original.ToReplayText());

        return AssertSchedulesEqualAsync(original, parsed);
    }

    /// <summary>Verifies operation names are lower-case in export regardless of how steps were constructed.</summary>
    [Test]
    public async Task ToReplayTextUsesLowercaseOperationNames()
    {
        var schedule = ReplaySchedule.Replay(ReplayStep.Hit("a", "b"), ReplayStep.Arrive("c", "d"), ReplayStep.Release("e", "f"));

        var text = schedule.ToReplayText();

        _ = await Assert.That(text).StartsWith("hit ");
        _ = await Assert.That(text).Contains(Environment.NewLine + "arrive ");
        _ = await Assert.That(text).Contains(Environment.NewLine + "release ");
        _ = await Assert.That(text).DoesNotContain("Hit");
        _ = await Assert.That(text).DoesNotContain("Arrive");
        _ = await Assert.That(text).DoesNotContain("Release");
    }

    private static async Task AssertSchedulesEqualAsync(ReplaySchedule expected, ReplaySchedule actual)
    {
        _ = await Assert.That(actual.Steps.Count).IsEqualTo(expected.Steps.Count);
        for (var index = 0; index < expected.Steps.Count; index++)
        {
            _ = await Assert.That(actual.Steps[index].Kind).IsEqualTo(expected.Steps[index].Kind);
            _ = await Assert.That(actual.Steps[index].WorkerId).IsEqualTo(expected.Steps[index].WorkerId);
            _ = await Assert.That(actual.Steps[index].ProbeName).IsEqualTo(expected.Steps[index].ProbeName);
        }
    }
}
