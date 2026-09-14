namespace Braid.Tests;

/// <summary>Covers <see cref="RunException.TryGetReplayText" /> behavior.</summary>
public sealed class BraidRunExceptionTryGetReplayTextTests : TestBase
{
    /// <summary>Verifies <see cref="RunException.ToString" /> still embeds replay lines when export succeeds.</summary>
    [Test]
    public async Task ToStringIncludesReplayTextWhenExportable()
    {
        var steps = new[] { ReplayStep.Hit("worker-1", "ready") };
        var exception = new RunException("failed", 1, 0, ["worker-1 forked"], steps, null);

        _ = await Assert.That(exception.TryGetReplayText(out var expectedText, out _)).IsTrue();

        var report = exception.ToString();
        _ = await Assert.That(report).Contains("Replay text:");
        foreach (var segment in expectedText.Split(Environment.NewLine))
            _ = await Assert.That(report).Contains(segment);
    }

    /// <summary>Verifies <see cref="RunException.ToString" /> keeps the generic unavailable line when export fails.</summary>
    [Test]
    public async Task ToStringReportsUnavailableNotExportable()
    {
        var exception = new RunException("failed", 1, 0, [], [ReplayStep.Hit("has space", "ready")], null);

        _ = await Assert.That(exception.TryGetReplayText(out _, out var apiError)).IsFalse();
        _ = await Assert.That(apiError).IsNotNull();

        var report = exception.ToString();
        _ = await Assert.That(report).Contains("Replay text unavailable");
        _ = await Assert.That(report).Contains("cannot be represented");
        _ = await Assert.That(report).DoesNotContain(apiError);
    }

    /// <summary>Verifies whitespace in probe name prevents replay-text export with a diagnostic error.</summary>
    [Test]
    public async Task FalseOnProbeWhitespace()
    {
        var exception = new RunException("failed", 1, 0, [], [ReplayStep.Hit("worker-1", "bad probe")], null);

        _ = await Assert.That(exception.TryGetReplayText(out var text, out var error)).IsFalse();
        _ = await Assert.That(text).IsEqualTo(string.Empty);
        _ = await Assert.That(error).IsNotNull();
        _ = await Assert.That(error).Contains("Probe name");
        _ = await Assert.That(error).Contains("whitespace");
    }

    /// <summary>Verifies whitespace in worker id prevents replay-text export with a diagnostic error.</summary>
    [Test]
    public async Task FalseOnWorkerWhitespace()
    {
        var exception = new RunException("failed", 1, 0, [], [ReplayStep.Hit("worker id", "ready")], null);

        _ = await Assert.That(exception.TryGetReplayText(out var text, out var error)).IsFalse();
        _ = await Assert.That(text).IsEqualTo(string.Empty);
        _ = await Assert.That(error).IsNotNull();
        _ = await Assert.That(error).Contains("Worker id");
        _ = await Assert.That(error).Contains("whitespace");
    }

    /// <summary>Verifies random-only (empty schedule) yields false with no export error.</summary>
    [Test]
    public async Task FalseWhenScheduleEmpty()
    {
        var exception = new RunException("failed", 1, 0, [], [], null);

        _ = await Assert.That(exception.TryGetReplayText(out var text, out var error)).IsFalse();
        _ = await Assert.That(text).IsEqualTo(string.Empty);
        _ = await Assert.That(error).IsNull();
    }

    /// <summary>Verifies a typed exportable schedule yields canonical replay text.</summary>
    [Test]
    public async Task TrueForExportable()
    {
        var steps = new[]
        {
            ReplayStep.Hit("worker-1", "after-read"),
            ReplayStep.Hit("worker-2", "after-read"),
        };

        var exception = new RunException("failed", 1, 0, [], steps, null);

        _ = await Assert.That(exception.TryGetReplayText(out var text, out var error)).IsTrue();
        _ = await Assert.That(error).IsNull();
        _ = await Assert.That(text).IsEqualTo(ReplaySchedule.Replay(steps).ToReplayText());
        _ = await Assert.That(text).Contains("hit worker-1 after-read");
    }
}
