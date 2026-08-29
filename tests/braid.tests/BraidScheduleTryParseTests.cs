using Xunit;

namespace Braid.Tests;

/// <summary>Covers <see cref="BraidSchedule.TryParse" /> behavior for valid and malformed inputs.</summary>
public sealed class BraidScheduleTryParseTests : TestBase
{
    /// <summary>Verifies try-parse does not throw for malformed inputs.</summary>
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
            AssertTryParseDoesNotThrow(text);
    }

    /// <summary>Verifies null input returns false from try-parse with a message.</summary>
    [Fact]
    public void TryParseNullReturnsFalseWithMessage()
    {
        var ok = BraidSchedule.TryParse(null, out var schedule, out var error);

        Assert.False(ok);
        Assert.Null(schedule);
        Assert.NotNull(error);
    }

    /// <summary>Verifies try-parse returns false for invalid schedules.</summary>
    [Fact]
    public void TryParseReturnsFalseForInvalidText()
    {
        var ok = BraidSchedule.TryParse("bogus a b", out var schedule, out var error);

        Assert.False(ok);
        Assert.Null(schedule);
        Assert.NotNull(error);
        Assert.Contains("unknown", error, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies try-parse returns a schedule for valid input.</summary>
    [Fact]
    public void TryParseReturnsScheduleForValidText()
    {
        var ok = BraidSchedule.TryParse("hit w-1 p1", out var schedule, out var error);

        Assert.True(ok);
        Assert.NotNull(schedule);
        Assert.Null(error);
        _ = Assert.Single(schedule.Steps);
    }
}
