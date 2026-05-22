using Xunit;

namespace Braid.Tests;

/// <summary>
/// Covers <see cref="BraidRunException.TryGetReplayText"/> behavior.
/// </summary>
public sealed class BraidRunExceptionTryGetReplayTextTests : TestBase
{
    /// <summary>
    /// Verifies a typed exportable schedule yields canonical replay text.
    /// </summary>
    [Fact]
    public void TryGetReplayText_returns_true_for_exportable_typed_schedule()
    {
        var steps = new[]
        {
            BraidStep.Hit("worker-1", "after-read"),
            BraidStep.Hit("worker-2", "after-read"),
        };

        var exception = new BraidRunException("failed", 1, 0, [], steps, null);

        Assert.True(exception.TryGetReplayText(out var text, out var error));
        Assert.Null(error);
        Assert.Equal(BraidSchedule.Replay(steps).ToReplayText(), text);
        Assert.Contains("hit worker-1 after-read", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies random-only (empty schedule) yields false with no export error.
    /// </summary>
    [Fact]
    public void TryGetReplayText_returns_false_when_schedule_is_empty()
    {
        var exception = new BraidRunException("failed", 1, 0, [], [], null);

        Assert.False(exception.TryGetReplayText(out var text, out var error));
        Assert.Equal(string.Empty, text);
        Assert.Null(error);
    }

    /// <summary>
    /// Verifies whitespace in worker id prevents replay-text export with a diagnostic error.
    /// </summary>
    [Fact]
    public void TryGetReplayText_returns_false_when_worker_id_has_whitespace()
    {
        var exception = new BraidRunException(
            "failed",
            1,
            0,
            [],
            [BraidStep.Hit("worker id", "ready")],
            null);

        Assert.False(exception.TryGetReplayText(out var text, out var error));
        Assert.Equal(string.Empty, text);
        Assert.NotNull(error);
        Assert.Contains("Worker id", error, StringComparison.Ordinal);
        Assert.Contains("whitespace", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies whitespace in probe name prevents replay-text export with a diagnostic error.
    /// </summary>
    [Fact]
    public void TryGetReplayText_returns_false_when_probe_name_has_whitespace()
    {
        var exception = new BraidRunException(
            "failed",
            1,
            0,
            [],
            [BraidStep.Hit("worker-1", "bad probe")],
            null);

        Assert.False(exception.TryGetReplayText(out var text, out var error));
        Assert.Equal(string.Empty, text);
        Assert.NotNull(error);
        Assert.Contains("Probe name", error, StringComparison.Ordinal);
        Assert.Contains("whitespace", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies <see cref="BraidRunException.ToString"/> still embeds replay lines when export succeeds.
    /// </summary>
    [Fact]
    public void ToString_includes_replay_text_when_exportable()
    {
        var steps = new[] { BraidStep.Hit("worker-1", "ready") };
        var exception = new BraidRunException("failed", 1, 0, ["worker-1 forked"], steps, null);

        Assert.True(exception.TryGetReplayText(out var expectedText, out _));

        var report = exception.ToString();
        Assert.Contains("Replay text:", report, StringComparison.Ordinal);
        foreach (var segment in expectedText.Split(Environment.NewLine))
        {
            Assert.Contains(segment, report, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Verifies <see cref="BraidRunException.ToString"/> keeps the generic unavailable line when export fails.
    /// </summary>
    [Fact]
    public void ToString_reports_unavailable_replay_text_when_not_exportable()
    {
        var exception = new BraidRunException(
            "failed",
            1,
            0,
            [],
            [BraidStep.Hit("has space", "ready")],
            null);

        Assert.False(exception.TryGetReplayText(out _, out var apiError));
        Assert.NotNull(apiError);

        var report = exception.ToString();
        Assert.Contains("Replay text unavailable", report, StringComparison.Ordinal);
        Assert.Contains("cannot be represented", report, StringComparison.Ordinal);
        Assert.DoesNotContain(apiError, report, StringComparison.Ordinal);
    }
}
