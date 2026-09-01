using Xunit;

namespace Braid.Tests;

/// <summary>Covers textual replay schedule parsing rejection of malformed inputs.</summary>
public sealed class BraidScheduleParseRejectionTests : TestBase
{
    /// <summary>Verifies comment-only input is rejected.</summary>
    [Fact]
    public void ParseRejectsCommentOnlyText()
    {
        var ex = Assertions.Expects<FormatException>(static () => ReplaySchedule.Parse("# only\n  # comments\n"));
        Assert.Contains("no replay steps", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies empty text is rejected.</summary>
    [Fact]
    public void ParseRejectsEmptyText()
    {
        var ex = Assertions.Expects<FormatException>(static () => ReplaySchedule.Parse(string.Empty));
        Assert.Contains("empty or contains only whitespace", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies extra tokens are rejected.</summary>
    [Fact]
    public void ParseRejectsExtraTokens()
    {
        var ex = Assertions.Expects<FormatException>(static () => ReplaySchedule.Parse("hit worker-1 ready extra"));

        Assert.Contains("line 1", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exactly 3 tokens", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>Verifies inline comments are treated as extra tokens.</summary>
    [Fact]
    public void ParseRejectsInlineComment()
    {
        var ex = Assertions.Expects<FormatException>(static () => ReplaySchedule.Parse("hit worker-1 ready # inline"));

        Assert.Contains("line 1", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exactly 3 tokens", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>Verifies a missing probe name is rejected.</summary>
    [Fact]
    public void ParseRejectsMissingProbe()
    {
        var ex = Assertions.Expects<FormatException>(static () => ReplaySchedule.Parse("hit worker-1"));

        Assert.Contains("line 1", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("probe", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies a missing worker id is rejected.</summary>
    [Fact]
    public void ParseRejectsMissingWorker()
    {
        var ex = Assertions.Expects<FormatException>(static () => ReplaySchedule.Parse("hit"));

        Assert.Contains("line 1", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("worker", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies null text throws from <see cref="ReplaySchedule.Parse" />.
    /// </summary>
    [Fact]
    public void ParseRejectsNullText() => _ = Assertions.Expects<ArgumentNullException>(static () => ReplaySchedule.Parse(NullTestValues.String));

    /// <summary>Verifies unknown operations are rejected with a line number.</summary>
    [Fact]
    public void ParseRejectsUnknownOperation()
    {
        var ex = Assertions.Expects<FormatException>(static () => ReplaySchedule.Parse("\nnoop worker-1 ready"));

        Assert.Contains("line 2", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unknown", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("noop", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>Verifies whitespace-only text is rejected.</summary>
    [Fact]
    public void ParseRejectsWhitespaceOnlyText()
    {
        var ex = Assertions.Expects<FormatException>(static () => ReplaySchedule.Parse("   \t  "));
        Assert.Contains("empty or contains only whitespace", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
