namespace Braid.Tests;

/// <summary>Covers braid schedule validation behavior.</summary>
public sealed class BraidScheduleTests : TestBase
{
    /// <summary>Verifies replay rejects blank probe names.</summary>
    [Test]
    public void ReplayThrowsForBlankProbeName()
    {
        _ = BraidAssertions.AssertExpects<ArgumentException>(static () => ReplaySchedule.Replay(new ReplayStep("worker-1", string.Empty)));
        _ = BraidAssertions.AssertExpects<ArgumentException>(static () => ReplaySchedule.Replay(new ReplayStep("worker-1", " ")));
    }

    /// <summary>Verifies replay rejects blank worker ids.</summary>
    [Test]
    public void ReplayThrowsForBlankWorkerId()
    {
        _ = BraidAssertions.AssertExpects<ArgumentException>(static () => ReplaySchedule.Replay(new ReplayStep(string.Empty, "ready")));
        _ = BraidAssertions.AssertExpects<ArgumentException>(static () => ReplaySchedule.Replay(new ReplayStep(" ", "ready")));
    }
}
