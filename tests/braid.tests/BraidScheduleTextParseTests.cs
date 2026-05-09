using Xunit;

namespace Braid.Tests;

/// <summary>
/// Covers textual replay schedule parsing.
/// </summary>
public sealed class BraidScheduleTextParseTests : TestBase
{
    /// <summary>
    /// Verifies a single hit line parses to a hit step.
    /// </summary>
    [Fact]
    public void ParseHitStep()
    {
        var schedule = BraidSchedule.Parse("hit worker-1 after-read");

        var step = Assert.Single(schedule.Steps);
        Assert.Equal(BraidStepKind.Hit, step.Kind);
        Assert.Equal("worker-1", step.WorkerId);
        Assert.Equal("after-read", step.ProbeName);
    }

    /// <summary>
    /// Verifies a single arrive line parses to an arrive step.
    /// </summary>
    [Fact]
    public void ParseArriveStep()
    {
        var schedule = BraidSchedule.Parse("arrive worker-1 cache-hit");

        var step = Assert.Single(schedule.Steps);
        Assert.Equal(BraidStepKind.Arrive, step.Kind);
        Assert.Equal("worker-1", step.WorkerId);
        Assert.Equal("cache-hit", step.ProbeName);
    }

    /// <summary>
    /// Verifies a single release line parses to a release step.
    /// </summary>
    [Fact]
    public void ParseReleaseStep()
    {
        var schedule = BraidSchedule.Parse("release worker-1 cache-hit");

        var step = Assert.Single(schedule.Steps);
        Assert.Equal(BraidStepKind.Release, step.Kind);
        Assert.Equal("worker-1", step.WorkerId);
        Assert.Equal("cache-hit", step.ProbeName);
    }

    /// <summary>
    /// Verifies multiple lines produce ordered steps.
    /// </summary>
    [Fact]
    public void ParseMultipleSteps()
    {
        const string text = """
                            hit worker-1 after-read
                            hit worker-2 after-read
                            arrive worker-1 before-write
                            """;

        var schedule = BraidSchedule.Parse(text);

        Assert.Equal(3, schedule.Steps.Count);
        Assert.Equal(BraidStepKind.Hit, schedule.Steps[0].Kind);
        Assert.Equal("worker-1", schedule.Steps[0].WorkerId);
        Assert.Equal(BraidStepKind.Hit, schedule.Steps[1].Kind);
        Assert.Equal("worker-2", schedule.Steps[1].WorkerId);
        Assert.Equal(BraidStepKind.Arrive, schedule.Steps[2].Kind);
        Assert.Equal("before-write", schedule.Steps[2].ProbeName);
    }

    /// <summary>
    /// Verifies blank lines are ignored.
    /// </summary>
    [Fact]
    public void ParseIgnoresEmptyLines()
    {
        var schedule = BraidSchedule.Parse("hit w p\n\nhit w2 p2");

        Assert.Equal(2, schedule.Steps.Count);
    }

    /// <summary>
    /// Verifies full-line comments are ignored.
    /// </summary>
    [Fact]
    public void ParseIgnoresFullLineComments()
    {
        const string text = """
                            # intro
                            hit worker-1 ready
                              # mid
                            hit worker-2 ready
                            """;

        var schedule = BraidSchedule.Parse(text);

        Assert.Equal(2, schedule.Steps.Count);
    }

    /// <summary>
    /// Verifies repeated whitespace between tokens is allowed.
    /// </summary>
    [Fact]
    public void ParseAllowsRepeatedWhitespace()
    {
        var schedule = BraidSchedule.Parse("hit\t worker-1   after-read");

        var step = Assert.Single(schedule.Steps);
        Assert.Equal("worker-1", step.WorkerId);
        Assert.Equal("after-read", step.ProbeName);
    }

    /// <summary>
    /// Verifies operation names are matched case-insensitively.
    /// </summary>
    [Fact]
    public void ParseOperationIsCaseInsensitive()
    {
        var a = BraidSchedule.Parse("HIT worker-1 x");
        var b = BraidSchedule.Parse("Hit worker-1 x");
        var c = BraidSchedule.Parse("hit worker-1 x");

        Assert.Equal(BraidStepKind.Hit, Assert.Single(a.Steps).Kind);
        Assert.Equal(BraidStepKind.Hit, Assert.Single(b.Steps).Kind);
        Assert.Equal(BraidStepKind.Hit, Assert.Single(c.Steps).Kind);

        var d = BraidSchedule.Parse("ARRIVE w p");
        var e = BraidSchedule.Parse("ReLeAsE w p");

        Assert.Equal(BraidStepKind.Arrive, Assert.Single(d.Steps).Kind);
        Assert.Equal(BraidStepKind.Release, Assert.Single(e.Steps).Kind);
    }

    /// <summary>
    /// Verifies worker id casing is preserved.
    /// </summary>
    [Fact]
    public void ParsePreservesWorkerCase()
    {
        var schedule = BraidSchedule.Parse("hit Worker-1 ready");

        Assert.Equal("Worker-1", Assert.Single(schedule.Steps).WorkerId);
    }

    /// <summary>
    /// Verifies probe name casing is preserved.
    /// </summary>
    [Fact]
    public void ParsePreservesProbeCase()
    {
        var schedule = BraidSchedule.Parse("hit worker-1 Cache-Hit");

        Assert.Equal("Cache-Hit", Assert.Single(schedule.Steps).ProbeName);
    }

    /// <summary>
    /// Verifies null text throws from <see cref="BraidSchedule.Parse"/>.
    /// </summary>
    [Fact]
    public void ParseRejectsNullText() => _ = Assert.Throws<ArgumentNullException>(static () => BraidSchedule.Parse(null!));

    /// <summary>
    /// Verifies empty text is rejected.
    /// </summary>
    [Fact]
    public void ParseRejectsEmptyText()
    {
        var ex = Assert.Throws<FormatException>(static () => BraidSchedule.Parse(string.Empty));
        Assert.NotEmpty(ex.Message);
    }

    /// <summary>
    /// Verifies whitespace-only text is rejected.
    /// </summary>
    [Fact]
    public void ParseRejectsWhitespaceOnlyText()
    {
        var ex = Assert.Throws<FormatException>(static () => BraidSchedule.Parse("   \t  "));
        Assert.NotEmpty(ex.Message);
    }

    /// <summary>
    /// Verifies comment-only input is rejected.
    /// </summary>
    [Fact]
    public void ParseRejectsCommentOnlyText()
    {
        var ex = Assert.Throws<FormatException>(static () => BraidSchedule.Parse("# only\n  # comments\n"));
        Assert.Contains("no replay steps", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies unknown operations are rejected with a line number.
    /// </summary>
    [Fact]
    public void ParseRejectsUnknownOperation()
    {
        var ex = Assert.Throws<FormatException>(static () => BraidSchedule.Parse("\nnoop worker-1 ready"));

        Assert.Contains("line 2", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unknown", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("noop", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a missing worker id is rejected.
    /// </summary>
    [Fact]
    public void ParseRejectsMissingWorker()
    {
        var ex = Assert.Throws<FormatException>(static () => BraidSchedule.Parse("hit"));

        Assert.Contains("line 1", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("worker", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies a missing probe name is rejected.
    /// </summary>
    [Fact]
    public void ParseRejectsMissingProbe()
    {
        var ex = Assert.Throws<FormatException>(static () => BraidSchedule.Parse("hit worker-1"));

        Assert.Contains("line 1", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("probe", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies extra tokens are rejected.
    /// </summary>
    [Fact]
    public void ParseRejectsExtraTokens()
    {
        var ex = Assert.Throws<FormatException>(static () => BraidSchedule.Parse("hit worker-1 ready extra"));

        Assert.Contains("line 1", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("3", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies inline comments are treated as extra tokens.
    /// </summary>
    [Fact]
    public void ParseRejectsInlineComment()
    {
        var ex = Assert.Throws<FormatException>(static () => BraidSchedule.Parse("hit worker-1 ready # inline"));

        Assert.Contains("line 1", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies try-parse returns false for invalid schedules.
    /// </summary>
    [Fact]
    public void TryParseReturnsFalseForInvalidText()
    {
        var ok = BraidSchedule.TryParse("bogus a b", out var schedule, out var error);

        Assert.False(ok);
        Assert.Null(schedule);
        Assert.NotNull(error);
        Assert.Contains("unknown", error, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies try-parse returns a schedule for valid input.
    /// </summary>
    [Fact]
    public void TryParseReturnsScheduleForValidText()
    {
        var ok = BraidSchedule.TryParse("hit w-1 p1", out var schedule, out var error);

        Assert.True(ok);
        Assert.NotNull(schedule);
        Assert.Null(error);
        _ = Assert.Single(schedule.Steps);
    }

    /// <summary>
    /// Verifies try-parse does not throw for malformed inputs.
    /// </summary>
    [Fact]
    public void TryParseDoesNotThrowForMalformedInput()
    {
        string?[] inputs =
        [
            null,
            string.Empty,
            "   ",
            "#\n#",
            "nope w p",
            "hit",
            "hit w",
            "hit w p x",
        ];

        foreach (var text in inputs)
        {
            var ex = Record.Exception(() => BraidSchedule.TryParse(text, out _, out _));
            Assert.Null(ex);
        }
    }

    /// <summary>
    /// Verifies null input returns false from try-parse with a message.
    /// </summary>
    [Fact]
    public void TryParseNullReturnsFalseWithMessage()
    {
        var ok = BraidSchedule.TryParse(null, out var schedule, out var error);

        Assert.False(ok);
        Assert.Null(schedule);
        Assert.NotNull(error);
    }
}
