using Xunit;

namespace Braid.Tests;

/// <summary>Covers textual replay schedule parsing for valid inputs.</summary>
public sealed class BraidScheduleParseTests : TestBase
{
    /// <summary>Verifies repeated whitespace between tokens is allowed.</summary>
    [Fact]
    public void ParseAllowsRepeatedWhitespace()
    {
        var schedule = ReplaySchedule.Parse("hit\t worker-1   after-read");

        var step = Assert.Single(schedule.Steps);
        Assert.Equal("worker-1", step.WorkerId);
        Assert.Equal("after-read", step.ProbeName);
    }

    /// <summary>Verifies a single arrive line parses to an arrive step.</summary>
    [Fact]
    public void ParseArriveStep()
    {
        var schedule = ReplaySchedule.Parse("arrive worker-1 cache-hit");

        var step = Assert.Single(schedule.Steps);
        Assert.Equal(ReplayStepKind.Arrive, step.Kind);
        Assert.Equal("worker-1", step.WorkerId);
        Assert.Equal("cache-hit", step.ProbeName);
    }

    /// <summary>Verifies a single hit line parses to a hit step.</summary>
    [Fact]
    public void ParseHitStep()
    {
        var schedule = ReplaySchedule.Parse("hit worker-1 after-read");

        var step = Assert.Single(schedule.Steps);
        Assert.Equal(ReplayStepKind.Hit, step.Kind);
        Assert.Equal("worker-1", step.WorkerId);
        Assert.Equal("after-read", step.ProbeName);
    }

    /// <summary>Verifies blank lines are ignored.</summary>
    [Fact]
    public void ParseIgnoresEmptyLines()
    {
        var schedule = ReplaySchedule.Parse("hit w p\n\nhit w2 p2");

        Assert.Equal(2, schedule.Steps.Count);
    }

    /// <summary>Verifies full-line comments are ignored.</summary>
    [Fact]
    public void ParseIgnoresFullLineComments()
    {
        const string text = "# intro\nhit worker-1 ready\n  # mid\nhit worker-2 ready\n";

        var schedule = ReplaySchedule.Parse(text);

        Assert.Equal(2, schedule.Steps.Count);
    }

    /// <summary>Verifies multiple lines produce ordered steps.</summary>
    [Fact]
    public void ParseMultipleSteps()
    {
        const string text = "hit worker-1 after-read\nhit worker-2 after-read\narrive worker-1 before-write\n";

        var schedule = ReplaySchedule.Parse(text);

        Assert.Equal(3, schedule.Steps.Count);
        Assert.Equal(ReplayStepKind.Hit, schedule.Steps[0].Kind);
        Assert.Equal("worker-1", schedule.Steps[0].WorkerId);
        Assert.Equal(ReplayStepKind.Hit, schedule.Steps[1].Kind);
        Assert.Equal("worker-2", schedule.Steps[1].WorkerId);
        Assert.Equal(ReplayStepKind.Arrive, schedule.Steps[2].Kind);
        Assert.Equal("before-write", schedule.Steps[2].ProbeName);
    }

    /// <summary>Verifies operation names are matched case-insensitively.</summary>
    [Fact]
    public void ParseOperationIsCaseInsensitive()
    {
        var a = ReplaySchedule.Parse("HIT worker-1 x");
        var b = ReplaySchedule.Parse("Hit worker-1 x");
        var c = ReplaySchedule.Parse("hit worker-1 x");

        Assert.Equal(ReplayStepKind.Hit, Assert.Single(a.Steps).Kind);
        Assert.Equal(ReplayStepKind.Hit, Assert.Single(b.Steps).Kind);
        Assert.Equal(ReplayStepKind.Hit, Assert.Single(c.Steps).Kind);

        var d = ReplaySchedule.Parse("ARRIVE w p");
        var e = ReplaySchedule.Parse("ReLeAsE w p");

        Assert.Equal(ReplayStepKind.Arrive, Assert.Single(d.Steps).Kind);
        Assert.Equal(ReplayStepKind.Release, Assert.Single(e.Steps).Kind);
    }

    /// <summary>Verifies probe name casing is preserved.</summary>
    [Fact]
    public void ParsePreservesProbeCase()
    {
        var schedule = ReplaySchedule.Parse("hit worker-1 Cache-Hit");

        Assert.Equal("Cache-Hit", Assert.Single(schedule.Steps).ProbeName);
    }

    /// <summary>Verifies worker id casing is preserved.</summary>
    [Fact]
    public void ParsePreservesWorkerCase()
    {
        var schedule = ReplaySchedule.Parse("hit Worker-1 ready");

        Assert.Equal("Worker-1", Assert.Single(schedule.Steps).WorkerId);
    }

    /// <summary>Verifies a single release line parses to a release step.</summary>
    [Fact]
    public void ParseReleaseStep()
    {
        var schedule = ReplaySchedule.Parse("release worker-1 cache-hit");

        var step = Assert.Single(schedule.Steps);
        Assert.Equal(ReplayStepKind.Release, step.Kind);
        Assert.Equal("worker-1", step.WorkerId);
        Assert.Equal("cache-hit", step.ProbeName);
    }
}
