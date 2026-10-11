namespace Braid.Tests;

/// <summary>Covers <see cref="ReplaySchedule.TryParse" /> behavior for valid and malformed inputs.</summary>
public sealed class BraidScheduleTryParseTests : TestBase
{
    /// <summary>Verifies try-parse does not throw for each malformed input.</summary>
    /// <param name="text">The malformed schedule text to attempt to parse.</param>
    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("#\n#")]
    [Arguments("nope w p")]
    [Arguments("hit")]
    [Arguments("hit w")]
    [Arguments("hit w p x")]
    public void DoesNotThrowForMalformedInput(string? text) => AssertTryParseDoesNotThrow(text);

    /// <summary>Verifies null input returns false from try-parse with a message.</summary>
    [Test]
    public async Task NullReturnsFalseWithMessage()
    {
        var ok = ReplaySchedule.TryParse(null, out var schedule, out var error);

        _ = await Assert.That(ok).IsFalse();
        _ = await Assert.That(schedule).IsNull();
        _ = await Assert.That(error).IsNotNull();
    }

    /// <summary>Verifies try-parse returns false for invalid schedules.</summary>
    [Test]
    public async Task ReturnsFalseForInvalidText()
    {
        var ok = ReplaySchedule.TryParse("bogus a b", out var schedule, out var error);

        _ = await Assert.That(ok).IsFalse();
        _ = await Assert.That(schedule).IsNull();
        _ = await Assert.That(error).IsNotNull();
        _ = await Assert.That(error).Contains("unknown", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies try-parse returns a schedule for valid input.</summary>
    [Test]
    public async Task ReturnsScheduleForValidText()
    {
        var ok = ReplaySchedule.TryParse("hit w-1 p1", out var schedule, out var error);

        _ = await Assert.That(ok).IsTrue();
        _ = await Assert.That(schedule).IsNotNull();
        _ = await Assert.That(error).IsNull();
        _ = await Assert.That(schedule.Steps).HasSingleItem();
    }
}
