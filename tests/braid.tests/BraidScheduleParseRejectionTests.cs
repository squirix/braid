namespace Braid.Tests;

/// <summary>Covers textual replay schedule parsing rejection of malformed inputs.</summary>
public sealed class BraidScheduleParseRejectionTests : TestBase
{
    /// <summary>Verifies comment-only input is rejected.</summary>
    [Test]
    public async Task ParseRejectsCommentOnlyText()
    {
        var ex = BraidAssertions.AssertExpects<FormatException>(static () => ReplaySchedule.Parse("# only\n  # comments\n"));
        _ = await Assert.That(ex.Message).Contains("no replay steps", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies empty text is rejected.</summary>
    [Test]
    public async Task ParseRejectsEmptyText()
    {
        var ex = BraidAssertions.AssertExpects<FormatException>(static () => ReplaySchedule.Parse(string.Empty));
        _ = await Assert.That(ex.Message).Contains("empty or contains only whitespace", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies extra tokens are rejected.</summary>
    [Test]
    public async Task ParseRejectsExtraTokens()
    {
        var ex = BraidAssertions.AssertExpects<FormatException>(static () => ReplaySchedule.Parse("hit worker-1 ready extra"));

        _ = await Assert.That(ex.Message).Contains("line 1", StringComparison.OrdinalIgnoreCase);
        _ = await Assert.That(ex.Message).Contains("exactly 3 tokens");
    }

    /// <summary>Verifies inline comments are treated as extra tokens.</summary>
    [Test]
    public async Task ParseRejectsInlineComment()
    {
        var ex = BraidAssertions.AssertExpects<FormatException>(static () => ReplaySchedule.Parse("hit worker-1 ready # inline"));

        _ = await Assert.That(ex.Message).Contains("line 1", StringComparison.OrdinalIgnoreCase);
        _ = await Assert.That(ex.Message).Contains("exactly 3 tokens");
    }

    /// <summary>Verifies a missing probe name is rejected.</summary>
    [Test]
    public async Task ParseRejectsMissingProbe()
    {
        var ex = BraidAssertions.AssertExpects<FormatException>(static () => ReplaySchedule.Parse("hit worker-1"));

        _ = await Assert.That(ex.Message).Contains("line 1", StringComparison.OrdinalIgnoreCase);
        _ = await Assert.That(ex.Message).Contains("probe", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies a missing worker id is rejected.</summary>
    [Test]
    public async Task ParseRejectsMissingWorker()
    {
        var ex = BraidAssertions.AssertExpects<FormatException>(static () => ReplaySchedule.Parse("hit"));

        _ = await Assert.That(ex.Message).Contains("line 1", StringComparison.OrdinalIgnoreCase);
        _ = await Assert.That(ex.Message).Contains("worker", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies null text throws from <see cref="ReplaySchedule.Parse" />.</summary>
    [Test]
    public void ParseRejectsNullText() => _ = BraidAssertions.AssertExpects<ArgumentNullException>(static () => ReplaySchedule.Parse(NullTestValues.String));

    /// <summary>Verifies unknown operations are rejected with a line number.</summary>
    [Test]
    public async Task ParseRejectsUnknownOperation()
    {
        var ex = BraidAssertions.AssertExpects<FormatException>(static () => ReplaySchedule.Parse("\nnoop worker-1 ready"));

        _ = await Assert.That(ex.Message).Contains("line 2", StringComparison.OrdinalIgnoreCase);
        _ = await Assert.That(ex.Message).Contains("unknown", StringComparison.OrdinalIgnoreCase);
        _ = await Assert.That(ex.Message).Contains("noop");
    }

    /// <summary>Verifies whitespace-only text is rejected.</summary>
    [Test]
    public async Task ParseRejectsWhitespaceOnlyText()
    {
        var ex = BraidAssertions.AssertExpects<FormatException>(static () => ReplaySchedule.Parse("   \t  "));
        _ = await Assert.That(ex.Message).Contains("empty or contains only whitespace", StringComparison.OrdinalIgnoreCase);
    }
}
