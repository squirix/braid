using Braid.Internal;
using JetBrains.Annotations;

namespace Braid;

/// <summary>
/// Represents a failure discovered during a braid run with reproducibility details.
/// Inner exceptions are preserved on the base <see cref="Exception" /> and summarized in <see cref="ToString" />.
/// </summary>
[PublicAPI]
public sealed class BraidRunException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BraidRunException" /> class.
    /// </summary>
    public BraidRunException()
        : this("A braid run failed.", 0, 0, [], null, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BraidRunException" /> class with a message.
    /// </summary>
    /// <param name="message">The exception message.</param>
    public BraidRunException(string message)
        : this(message, 0, 0, [], null, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BraidRunException" /> class with a message and inner exception.
    /// </summary>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The underlying exception.</param>
    public BraidRunException(string message, Exception innerException)
        : this(message, 0, 0, [], null, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BraidRunException" /> class.
    /// </summary>
    /// <param name="message">The exception message.</param>
    /// <param name="seed">The seed used for the failing iteration.</param>
    /// <param name="iteration">The failing iteration index.</param>
    /// <param name="trace">The recorded scheduling trace.</param>
    /// <param name="schedule">The configured replay schedule.</param>
    /// <param name="innerException">The underlying exception.</param>
    /// <param name="schedulerDiagnostics">Scheduler state captured at failure time, when available.</param>
    /// <param name="failureOrigin">Whether the failure came from user test code or braid infrastructure.</param>
    public BraidRunException(
        string message,
        int seed,
        int iteration,
        IReadOnlyList<string> trace,
        IReadOnlyList<BraidStep>? schedule,
        Exception? innerException,
        BraidSchedulerDiagnostics? schedulerDiagnostics = null,
        BraidRunFailureOrigin failureOrigin = BraidRunFailureOrigin.Scheduler)
        : base(message, innerException)
    {
        ArgumentNullException.ThrowIfNull(trace);

        Seed = seed;
        Iteration = iteration;
        Trace = Array.AsReadOnly([.. trace]);
        Schedule = schedule is null ? Array.Empty<BraidStep>() : Array.AsReadOnly([.. schedule]);
        SchedulerDiagnostics = schedulerDiagnostics;
        FailureOrigin = failureOrigin;
    }

    /// <summary>Gets whether the failure originated from user test code or braid infrastructure.</summary>
    public BraidRunFailureOrigin FailureOrigin { get; }

    /// <summary>Gets the zero-based failing iteration index.</summary>
    public int Iteration { get; }

    /// <summary>Gets the configured replay schedule, or an empty list when random scheduling was used.</summary>
    public IReadOnlyList<BraidStep> Schedule { get; }

    /// <summary>Gets scheduler diagnostics captured when the failure was recorded, when available.</summary>
    public BraidSchedulerDiagnostics? SchedulerDiagnostics { get; }

    /// <summary>Gets the seed used for the failing iteration.</summary>
    public int Seed { get; }

    /// <summary>Gets the recorded scheduling trace for the failing iteration.</summary>
    public IReadOnlyList<string> Trace { get; }

    /// <inheritdoc />
    public override string ToString()
    {
        var lines = new List<string>
        {
            Message,
            $"Seed: {Seed}",
            $"Iteration: {Iteration}",
        };

        if (Schedule.Count > 0)
        {
            lines.Add("Schedule:");
            for (var index = 0; index < Schedule.Count; index++)
            {
                var step = Schedule[index];
                lines.Add(step.Kind is BraidStepKind.Hit ? $"  {index + 1}. {step.WorkerId} @ {step.ProbeName}" : $"  {index + 1}. {step.Kind} {step.WorkerId} @ {step.ProbeName}");
            }

            lines.Add("Replay text:");
            if (TryGetReplayText(out var replayText, out var replayError))
            {
                if (replayText.Length > 0)
                    lines.AddRange(replayText.Split(Environment.NewLine));
            }
            else if (replayError != null)
            {
                lines.Add("Replay text unavailable: schedule contains values that cannot be represented in replay text.");
            }
        }

        AppendSchedulerDiagnosticsLines(lines, SchedulerDiagnostics);

        lines.Add("Trace:");
        for (var index = 0; index < Trace.Count; index++)
            lines.Add($"  {index + 1}. {Trace[index]}");

        if (InnerException == null)
            return string.Join(Environment.NewLine, lines);
        lines.Add("Inner exception:");
        lines.Add($"  {InnerException.GetType().FullName}: {InnerException.Message}");
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Attempts to obtain canonical replay text for the configured typed schedule (same format as <see cref="BraidSchedule.Parse(string)" /> accepts).
    /// </summary>
    /// <param name="text">When this method returns <see langword="true" />, the exportable replay text. Otherwise <see cref="string.Empty" />.</param>
    /// <param name="error">
    /// When this method returns <see langword="false" /> because the schedule cannot be exported (for example whitespace in worker id or probe name),
    /// a diagnostic message; otherwise <see langword="null" /> (including when no typed schedule was configured).
    /// </param>
    /// <returns>
    /// <see langword="true" /> if <see cref="Schedule" /> is non-empty and <see cref="BraidSchedule.ToReplayText" /> succeeds; otherwise <see langword="false" />.
    /// </returns>
    public bool TryGetReplayText(out string text, out string? error)
    {
        text = string.Empty;
        error = null;

        if (Schedule.Count == 0)
            return false;

        try
        {
            var replaySchedule = BraidSchedule.Replay(Schedule);
            text = replaySchedule.ToReplayText();
            return true;
        }
        catch (InvalidOperationException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static void AppendSchedulerDiagnosticsContent(List<string> lines, BraidSchedulerDiagnostics diagnostics)
    {
        if (diagnostics.HasReplaySchedule)
        {
            lines.Add("Last matched replay step:");
            lines.Add(
                diagnostics is { LastMatchedReplayStep: { } lastStep, LastMatchedReplayStepOneBased: { } stepNumber }
                    ? $"  {stepNumber}. {ReplayFormat.CanonicalStepLine(lastStep)}" : "  none");
        }

        if (diagnostics.WaitingWorkers.Count > 0)
        {
            lines.Add("Waiting workers:");
            for (var index = 0; index < diagnostics.WaitingWorkers.Count; index++)
            {
                var worker = diagnostics.WaitingWorkers[index];
                lines.Add($"  {worker.WorkerId} @ {worker.ProbeName}");
            }
        }

        if (diagnostics.HeldWorkers.Count > 0)
        {
            lines.Add("Held workers:");
            for (var index = 0; index < diagnostics.HeldWorkers.Count; index++)
            {
                var worker = diagnostics.HeldWorkers[index];
                lines.Add($"  {worker.WorkerId} @ {worker.ProbeName}");
            }
        }

        if (diagnostics.UnusedReplaySteps.Count == 0)
            return;

        lines.Add("Unused replay steps:");
        for (var index = 0; index < diagnostics.UnusedReplaySteps.Count; index++)
        {
            var (oneBasedIndex, step) = diagnostics.UnusedReplaySteps[index];
            lines.Add($"  {oneBasedIndex}. {ReplayFormat.CanonicalStepLine(step)}");
        }
    }

    private static void AppendSchedulerDiagnosticsLines(List<string> lines, BraidSchedulerDiagnostics? diagnostics)
    {
        if (diagnostics == null)
            return;

        AppendSchedulerDiagnosticsContent(lines, diagnostics);
    }
}
