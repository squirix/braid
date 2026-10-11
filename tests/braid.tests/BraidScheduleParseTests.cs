namespace Braid.Tests;

/// <summary>Covers textual replay schedule parsing for valid inputs.</summary>
public sealed class BraidScheduleParseTests : TestBase
{
    /// <summary>Verifies repeated whitespace between tokens is allowed.</summary>
    [Test]
    public async Task ParseAllowsRepeatedWhitespace()
    {
        var schedule = ReplaySchedule.Parse("hit\t worker-1   after-read");

        var step = await Assert.That(schedule.Steps).HasSingleItem();
        _ = await Assert.That(step.WorkerId).IsEqualTo("worker-1");
        _ = await Assert.That(step.ProbeName).IsEqualTo("after-read");
    }

    /// <summary>Verifies a single arrive line parses to an arrive step.</summary>
    [Test]
    public async Task ParseArriveStep()
    {
        var schedule = ReplaySchedule.Parse("arrive worker-1 cache-hit");

        var step = await Assert.That(schedule.Steps).HasSingleItem();
        _ = await Assert.That(step.Kind).IsEqualTo(ReplayStepKind.Arrive);
        _ = await Assert.That(step.WorkerId).IsEqualTo("worker-1");
        _ = await Assert.That(step.ProbeName).IsEqualTo("cache-hit");
    }

    /// <summary>Verifies a single hit line parses to a hit step.</summary>
    [Test]
    public async Task ParseHitStep()
    {
        var schedule = ReplaySchedule.Parse("hit worker-1 after-read");

        var step = await Assert.That(schedule.Steps).HasSingleItem();
        _ = await Assert.That(step.Kind).IsEqualTo(ReplayStepKind.Hit);
        _ = await Assert.That(step.WorkerId).IsEqualTo("worker-1");
        _ = await Assert.That(step.ProbeName).IsEqualTo("after-read");
    }

    /// <summary>Verifies blank lines are ignored.</summary>
    [Test]
    public async Task ParseIgnoresEmptyLines()
    {
        var schedule = ReplaySchedule.Parse("hit w p\n\nhit w2 p2");

        _ = await Assert.That(schedule.Steps.Count).IsEqualTo(2);
    }

    /// <summary>Verifies full-line comments are ignored.</summary>
    [Test]
    public async Task ParseIgnoresFullLineComments()
    {
        const string text = "# intro\nhit worker-1 ready\n  # mid\nhit worker-2 ready\n";

        var schedule = ReplaySchedule.Parse(text);

        _ = await Assert.That(schedule.Steps.Count).IsEqualTo(2);
    }

    /// <summary>Verifies multiple lines produce ordered steps.</summary>
    [Test]
    public async Task ParseMultipleSteps()
    {
        const string text = "hit worker-1 after-read\nhit worker-2 after-read\narrive worker-1 before-write\n";

        var schedule = ReplaySchedule.Parse(text);

        _ = await Assert.That(schedule.Steps.Count).IsEqualTo(3);
        _ = await Assert.That(schedule.Steps[0].Kind).IsEqualTo(ReplayStepKind.Hit);
        _ = await Assert.That(schedule.Steps[0].WorkerId).IsEqualTo("worker-1");
        _ = await Assert.That(schedule.Steps[1].Kind).IsEqualTo(ReplayStepKind.Hit);
        _ = await Assert.That(schedule.Steps[1].WorkerId).IsEqualTo("worker-2");
        _ = await Assert.That(schedule.Steps[2].Kind).IsEqualTo(ReplayStepKind.Arrive);
        _ = await Assert.That(schedule.Steps[2].ProbeName).IsEqualTo("before-write");
    }

    /// <summary>Verifies operation names are matched case-insensitively.</summary>
    [Test]
    public async Task ParseOperationIsCaseInsensitive()
    {
        var a = ReplaySchedule.Parse("HIT worker-1 x");
        var b = ReplaySchedule.Parse("Hit worker-1 x");
        var c = ReplaySchedule.Parse("hit worker-1 x");

        var stepA = await Assert.That(a.Steps).HasSingleItem();
        _ = await Assert.That(stepA.Kind).IsEqualTo(ReplayStepKind.Hit);
        var stepB = await Assert.That(b.Steps).HasSingleItem();
        _ = await Assert.That(stepB.Kind).IsEqualTo(ReplayStepKind.Hit);
        var stepC = await Assert.That(c.Steps).HasSingleItem();
        _ = await Assert.That(stepC.Kind).IsEqualTo(ReplayStepKind.Hit);

        var d = ReplaySchedule.Parse("ARRIVE w p");
        var e = ReplaySchedule.Parse("ReLeAsE w p");

        var stepD = await Assert.That(d.Steps).HasSingleItem();
        _ = await Assert.That(stepD.Kind).IsEqualTo(ReplayStepKind.Arrive);
        var stepE = await Assert.That(e.Steps).HasSingleItem();
        _ = await Assert.That(stepE.Kind).IsEqualTo(ReplayStepKind.Release);
    }

    /// <summary>Verifies probe name casing is preserved.</summary>
    [Test]
    public async Task ParsePreservesProbeCase()
    {
        var schedule = ReplaySchedule.Parse("hit worker-1 Cache-Hit");

        var probeStep = await Assert.That(schedule.Steps).HasSingleItem();
        _ = await Assert.That(probeStep.ProbeName).IsEqualTo("Cache-Hit");
    }

    /// <summary>Verifies worker id casing is preserved.</summary>
    [Test]
    public async Task ParsePreservesWorkerCase()
    {
        var schedule = ReplaySchedule.Parse("hit Worker-1 ready");

        var workerStep = await Assert.That(schedule.Steps).HasSingleItem();
        _ = await Assert.That(workerStep.WorkerId).IsEqualTo("Worker-1");
    }

    /// <summary>Verifies a single release line parses to a release step.</summary>
    [Test]
    public async Task ParseReleaseStep()
    {
        var schedule = ReplaySchedule.Parse("release worker-1 cache-hit");

        var step = await Assert.That(schedule.Steps).HasSingleItem();
        _ = await Assert.That(step.Kind).IsEqualTo(ReplayStepKind.Release);
        _ = await Assert.That(step.WorkerId).IsEqualTo("worker-1");
        _ = await Assert.That(step.ProbeName).IsEqualTo("cache-hit");
    }
}
