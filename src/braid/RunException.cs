using Braid.Internal;
using JetBrains.Annotations;

namespace Braid;

/// <summary>
/// Represents a failure discovered during a braid run with reproducibility details.
/// Inner exceptions are preserved on the base <see cref="Exception" /> and summarized in <see cref="ToString" />.
/// </summary>
[PublicAPI]
public sealed class RunException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RunException" /> class.
    /// </summary>
    public RunException()
        : this("A braid run failed.", new RunExceptionContext(0, 0, [], []))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RunException" /> class with a message.
    /// </summary>
    /// <param name="message">The exception message.</param>
    public RunException(string message)
        : this(message, new RunExceptionContext(0, 0, [], []))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RunException" /> class with a message and inner exception.
    /// </summary>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The underlying exception.</param>
    public RunException(string message, Exception innerException)
        : this(message, new RunExceptionContext(0, 0, [], []), innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RunException" /> class.
    /// </summary>
    /// <param name="message">The exception message.</param>
    /// <param name="seed">The seed used for the failing iteration.</param>
    /// <param name="iteration">The failing iteration index.</param>
    /// <param name="trace">The recorded scheduling trace.</param>
    /// <param name="schedule">The configured replay schedule.</param>
    /// <param name="innerException">The underlying exception.</param>
    public RunException(
        string message,
        int seed,
        int iteration,
        IReadOnlyList<string> trace,
        IReadOnlyList<ReplayStep>? schedule,
        Exception? innerException)
        : this(message, new RunExceptionContext(seed, iteration, trace, schedule ?? []), innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RunException" /> class.
    /// </summary>
    /// <param name="message">The exception message.</param>
    /// <param name="context">Reproducibility context for the failure.</param>
    /// <param name="innerException">The underlying exception.</param>
    /// <param name="failureOrigin">Whether the failure came from user test code or braid infrastructure.</param>
    public RunException(
        string message,
        RunExceptionContext context,
        Exception? innerException = null,
        RunFailureOrigin failureOrigin = RunFailureOrigin.Scheduler)
        : base(message, innerException)
    {
        ArgumentNullException.ThrowIfNull(context);
        Context = context;
        FailureOrigin = failureOrigin;
    }

    /// <summary>Gets whether the failure originated from user test code or braid infrastructure.</summary>
    public RunFailureOrigin FailureOrigin { get; }

    /// <summary>Gets the reproducibility context for the failure.</summary>
    public RunExceptionContext Context { get; }

    /// <summary>Gets the zero-based failing iteration index.</summary>
    public int Iteration => Context.Iteration;

    /// <summary>Gets the configured replay schedule, or an empty list when random scheduling was used.</summary>
    public IReadOnlyList<ReplayStep> Schedule => Context.Schedule;

    /// <summary>Gets scheduler diagnostics captured when the failure was recorded, when available.</summary>
    public SchedulerDiagnostics? SchedulerDiagnostics => Context.SchedulerDiagnostics;

    /// <summary>Gets the seed used for the failing iteration.</summary>
    public int Seed => Context.Seed;

    /// <summary>Gets the recorded scheduling trace for the failing iteration.</summary>
    public IReadOnlyList<string> Trace => Context.Trace;

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
                lines.Add(step.Kind is ReplayStepKind.Hit ? $"  {index + 1}. {step.WorkerId} @ {step.ProbeName}" : $"  {index + 1}. {step.Kind} {step.WorkerId} @ {step.ProbeName}");
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
    /// Attempts to obtain canonical replay text for the configured typed schedule (same format as <see cref="ReplaySchedule.Parse(string)" /> accepts).
    /// </summary>
    /// <param name="text">When this method returns <see langword="true" />, the exportable replay text. Otherwise <see cref="string.Empty" />.</param>
    /// <param name="error">
    /// When this method returns <see langword="false" /> because the schedule cannot be exported (for example whitespace in worker id or probe name),
    /// a diagnostic message; otherwise <see langword="null" /> (including when no typed schedule was configured).
    /// </param>
    /// <returns>
    /// <see langword="true" /> if <see cref="Schedule" /> is non-empty and <see cref="ReplaySchedule.ToReplayText" /> succeeds; otherwise <see langword="false" />.
    /// </returns>
    public bool TryGetReplayText(out string text, out string? error)
    {
        text = string.Empty;
        error = null;

        if (Schedule.Count == 0)
            return false;

        try
        {
            var replaySchedule = ReplaySchedule.Replay(Schedule);
            text = replaySchedule.ToReplayText();
            return true;
        }
        catch (InvalidOperationException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static void AppendSchedulerDiagnosticsContent(List<string> lines, SchedulerDiagnostics diagnostics)
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

    private static void AppendSchedulerDiagnosticsLines(List<string> lines, SchedulerDiagnostics? diagnostics)
    {
        if (diagnostics == null)
            return;

        AppendSchedulerDiagnosticsContent(lines, diagnostics);
    }
}
