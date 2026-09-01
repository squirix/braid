using Xunit;

namespace Braid.Tests;

/// <summary>Covers braid schedule validation behavior.</summary>
public sealed class BraidScheduleTests : TestBase
{
    /// <summary>Verifies replay rejects blank probe names.</summary>
    [Fact]
    public void ReplayThrowsForBlankProbeName()
    {
        _ = Assertions.Expects<ArgumentException>(static () => ReplaySchedule.Replay(new ReplayStep("worker-1", string.Empty)));
        _ = Assertions.Expects<ArgumentException>(static () => ReplaySchedule.Replay(new ReplayStep("worker-1", " ")));
    }

    /// <summary>Verifies replay rejects blank worker ids.</summary>
    [Fact]
    public void ReplayThrowsForBlankWorkerId()
    {
        _ = Assertions.Expects<ArgumentException>(static () => ReplaySchedule.Replay(new ReplayStep(string.Empty, "ready")));
        _ = Assertions.Expects<ArgumentException>(static () => ReplaySchedule.Replay(new ReplayStep(" ", "ready")));
    }
}
