using Xunit;

namespace Braid.Tests;

/// <summary>Covers <see cref="ReplaySchedule.TryParse" /> behavior for valid and malformed inputs.</summary>
public sealed class BraidScheduleTryParseTests : TestBase
{
    /// <summary>Verifies try-parse does not throw for each malformed input.</summary>
    /// <param name="text">The malformed schedule text to attempt to parse.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#\n#")]
    [InlineData("nope w p")]
    [InlineData("hit")]
    [InlineData("hit w")]
    [InlineData("hit w p x")]
    public void TryParseDoesNotThrowForMalformedInput(string? text) => AssertTryParseDoesNotThrow(text);

    /// <summary>Verifies null input returns false from try-parse with a message.</summary>
    [Fact]
    public void TryParseNullReturnsFalseWithMessage()
    {
        var ok = ReplaySchedule.TryParse(null, out var schedule, out var error);

        Assert.False(ok);
        Assert.Null(schedule);
        Assert.NotNull(error);
    }

    /// <summary>Verifies try-parse returns false for invalid schedules.</summary>
    [Fact]
    public void TryParseReturnsFalseForInvalidText()
    {
        var ok = ReplaySchedule.TryParse("bogus a b", out var schedule, out var error);

        Assert.False(ok);
        Assert.Null(schedule);
        Assert.NotNull(error);
        Assert.Contains("unknown", error, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies try-parse returns a schedule for valid input.</summary>
    [Fact]
    public void TryParseReturnsScheduleForValidText()
    {
        var ok = ReplaySchedule.TryParse("hit w-1 p1", out var schedule, out var error);

        Assert.True(ok);
        Assert.NotNull(schedule);
        Assert.Null(error);
        _ = Assert.Single(schedule.Steps);
    }
}
