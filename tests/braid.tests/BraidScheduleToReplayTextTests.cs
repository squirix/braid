using Xunit;

namespace Braid.Tests;

/// <summary>
/// Covers canonical replay text export from <see cref="BraidSchedule"/>.
/// </summary>
public sealed class BraidScheduleToReplayTextTests : TestBase
{
    /// <summary>
    /// Verifies a hit step exports as a hit line.
    /// </summary>
    [Fact]
    public void ToReplayTextReturnsHitStep()
    {
        var schedule = BraidSchedule.Replay(BraidStep.Hit("worker-1", "after-read"));

        Assert.Equal("hit worker-1 after-read", schedule.ToReplayText());
    }

    /// <summary>
    /// Verifies an arrive step exports as an arrive line.
    /// </summary>
    [Fact]
    public void ToReplayTextReturnsArriveStep()
    {
        var schedule = BraidSchedule.Replay(BraidStep.Arrive("worker-1", "cache-hit"));

        Assert.Equal("arrive worker-1 cache-hit", schedule.ToReplayText());
    }

    /// <summary>
    /// Verifies a release step exports as a release line.
    /// </summary>
    [Fact]
    public void ToReplayTextReturnsReleaseStep()
    {
        var schedule = BraidSchedule.Replay(BraidStep.Release("worker-1", "cache-hit"));

        Assert.Equal("release worker-1 cache-hit", schedule.ToReplayText());
    }

    /// <summary>
    /// Verifies multiple steps export in order with platform newlines.
    /// </summary>
    [Fact]
    public void ToReplayTextReturnsMultipleStepsInOrder()
    {
        var schedule = BraidSchedule.Replay(
            BraidStep.Hit("worker-1", "after-read"),
            BraidStep.Arrive("worker-2", "before-write"),
            BraidStep.Release("worker-2", "before-write"));

        Assert.Equal(
            "hit worker-1 after-read" + Environment.NewLine +
            "arrive worker-2 before-write" + Environment.NewLine +
            "release worker-2 before-write",
            schedule.ToReplayText());
    }

    /// <summary>
    /// Verifies operation names are lower-case in export regardless of how steps were constructed.
    /// </summary>
    [Fact]
    public void ToReplayTextUsesLowercaseOperationNames()
    {
        var schedule = BraidSchedule.Replay(
            BraidStep.Hit("a", "b"),
            BraidStep.Arrive("c", "d"),
            BraidStep.Release("e", "f"));

        var text = schedule.ToReplayText();

        Assert.StartsWith("hit ", text, StringComparison.Ordinal);
        Assert.Contains(Environment.NewLine + "arrive ", text, StringComparison.Ordinal);
        Assert.Contains(Environment.NewLine + "release ", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Hit", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Arrive", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Release", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies exported text has no trailing whitespace.
    /// </summary>
    [Fact]
    public void ToReplayTextDoesNotAddTrailingWhitespace()
    {
        var schedule = BraidSchedule.Replay(
            BraidStep.Hit("w1", "p1"),
            BraidStep.Hit("w2", "p2"));

        var text = schedule.ToReplayText();

        Assert.False(char.IsWhiteSpace(text[^1]));
        Assert.DoesNotContain(" \n", text, StringComparison.Ordinal);
        Assert.DoesNotContain(" \r", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies hit schedules round-trip through text.
    /// </summary>
    [Fact]
    public void ToReplayTextRoundTripsHit()
    {
        var original = BraidSchedule.Replay(BraidStep.Hit("worker-1", "after-read"));

        var parsed = BraidSchedule.Parse(original.ToReplayText());

        AssertSchedulesEqual(original, parsed);
    }

    /// <summary>
    /// Verifies arrive and release schedules round-trip through text.
    /// </summary>
    [Fact]
    public void ToReplayTextRoundTripsArriveRelease()
    {
        var original = BraidSchedule.Replay(
            BraidStep.Arrive("worker-1", "A"),
            BraidStep.Hit("worker-2", "B"),
            BraidStep.Release("worker-1", "A"));

        var parsed = BraidSchedule.Parse(original.ToReplayText());

        AssertSchedulesEqual(original, parsed);
    }

    /// <summary>
    /// Verifies multi-step schedules round-trip through text.
    /// </summary>
    [Fact]
    public void ToReplayTextRoundTripsMultipleSteps()
    {
        var original = BraidSchedule.Replay(
            BraidStep.Hit("worker-1", "after-read"),
            BraidStep.Arrive("worker-2", "before-write"),
            BraidStep.Release("worker-2", "before-write"));

        var parsed = BraidSchedule.Parse(original.ToReplayText());

        AssertSchedulesEqual(original, parsed);
    }

    /// <summary>
    /// Verifies worker id casing is preserved in export and round-trip.
    /// </summary>
    [Fact]
    public void ToReplayTextPreservesWorkerCase()
    {
        var original = BraidSchedule.Replay(BraidStep.Hit("Worker-1", "x"));

        var text = original.ToReplayText();

        Assert.Equal("hit Worker-1 x", text);

        var parsed = BraidSchedule.Parse(text);
        Assert.Equal("Worker-1", Assert.Single(parsed.Steps).WorkerId);
    }

    /// <summary>
    /// Verifies probe name casing is preserved in export and round-trip.
    /// </summary>
    [Fact]
    public void ToReplayTextPreservesProbeCase()
    {
        var original = BraidSchedule.Replay(BraidStep.Hit("w", "Cache-Hit"));

        var text = original.ToReplayText();

        Assert.Equal("hit w Cache-Hit", text);

        var parsed = BraidSchedule.Parse(text);
        Assert.Equal("Cache-Hit", Assert.Single(parsed.Steps).ProbeName);
    }

    /// <summary>
    /// Verifies worker ids containing whitespace cannot be exported.
    /// </summary>
    [Fact]
    public void ToReplayTextRejectsWorkerWithWhitespace()
    {
        var schedule = BraidSchedule.Replay(BraidStep.Hit("worker 1", "p"));

        var ex = Assert.Throws<InvalidOperationException>(schedule.ToReplayText);

        Assert.Contains("worker", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("whitespace", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies probe names containing whitespace cannot be exported.
    /// </summary>
    [Fact]
    public void ToReplayTextRejectsProbeWithWhitespace()
    {
        var schedule = BraidSchedule.Replay(BraidStep.Hit("w", "probe name"));

        var ex = Assert.Throws<InvalidOperationException>(schedule.ToReplayText);

        Assert.Contains("probe", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("whitespace", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies an empty typed schedule exports to an empty string.
    /// </summary>
    [Fact]
    public void ToReplayTextReturnsEmptyStringForEmptySchedule()
    {
        var schedule = BraidSchedule.Replay();

        Assert.Equal(string.Empty, schedule.ToReplayText());
    }

    private static void AssertSchedulesEqual(BraidSchedule expected, BraidSchedule actual)
    {
        Assert.Equal(expected.Steps.Count, actual.Steps.Count);
        for (var index = 0; index < expected.Steps.Count; index++)
        {
            Assert.Equal(expected.Steps[index].Kind, actual.Steps[index].Kind);
            Assert.Equal(expected.Steps[index].WorkerId, actual.Steps[index].WorkerId);
            Assert.Equal(expected.Steps[index].ProbeName, actual.Steps[index].ProbeName);
        }
    }
}
