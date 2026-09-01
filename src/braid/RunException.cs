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
    public IReadOnlyList<ReplayStep> Steps => Context.Steps;

    /// <summary>Gets scheduler diagnostics captured when the failure was recorded, when available.</summary>
    public SchedulerDiagnostics? SchedulerDiagnostics => Context.SchedulerDiagnostics;

    /// <summary>Gets the seed used for the failing iteration.</summary>
    public int Seed => Context.Seed;

    /// <summary>Gets the recorded scheduling trace for the failing iteration.</summary>
    public IReadOnlyList<string> Traces => Context.Traces;

    /// <inheritdoc />
    public override string ToString()
    {
        var lines = new List<string>
        {
            Message,
            $"Seed: {Seed}",
            $"Iteration: {Iteration}",
        };

        AppendScheduleSection(lines);
        AppendSchedulerDiagnosticsLines(lines, SchedulerDiagnostics);
        AppendTraceSection(lines);
        AppendInnerExceptionSection(lines);

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
    /// <see langword="true" /> if <see cref="Steps" /> is non-empty and <see cref="ReplaySchedule.ToReplayText" /> succeeds; otherwise <see langword="false" />.
    /// </returns>
    public bool TryGetReplayText(out string text, out string? error)
    {
        text = string.Empty;
        error = null;

        if (Steps.Count == 0)
            return false;

        try
        {
            var replaySchedule = ReplaySchedule.Replay(Steps);
            text = replaySchedule.ToReplayText();
            return true;
        }
        catch (InvalidOperationException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static void AppendSchedulerDiagnosticsLines(List<string> lines, SchedulerDiagnostics? diagnostics)
    {
        if (diagnostics == null)
            return;

        AppendSchedulerDiagnosticsContent(lines, diagnostics);
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

        AppendProbeWaitDiagnostics(lines, "Waiting workers:", diagnostics.WaitingWorkers);
        AppendProbeWaitDiagnostics(lines, "Held workers:", diagnostics.HeldWorkers);
        AppendUnusedReplayStepDiagnostics(lines, diagnostics.UnusedReplaySteps);
    }

    private static void AppendProbeWaitDiagnostics(List<string> lines, string header, IReadOnlyList<ProbeWaitDiagnostic> workers)
    {
        if (workers.Count == 0)
            return;

        lines.Add(header);
        for (var index = 0; index < workers.Count; index++)
        {
            var worker = workers[index];
            lines.Add($"  {worker.WorkerId} @ {worker.ProbeName}");
        }
    }

    private static void AppendUnusedReplayStepDiagnostics(List<string> lines, IReadOnlyList<(int OneBasedIndex, ReplayStep Step)> steps)
    {
        if (steps.Count == 0)
            return;

        lines.Add("Unused replay steps:");
        for (var index = 0; index < steps.Count; index++)
        {
            var (oneBasedIndex, step) = steps[index];
            lines.Add($"  {oneBasedIndex}. {ReplayFormat.CanonicalStepLine(step)}");
        }
    }

    private void AppendScheduleSection(List<string> lines)
    {
        if (Steps.Count == 0)
            return;

        lines.Add("Schedule:");
        for (var index = 0; index < Steps.Count; index++)
        {
            var step = Steps[index];
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

    private void AppendTraceSection(List<string> lines)
    {
        lines.Add("Trace:");
        for (var index = 0; index < Traces.Count; index++)
            lines.Add($"  {index + 1}. {Traces[index]}");
    }

    private void AppendInnerExceptionSection(List<string> lines)
    {
        if (InnerException == null)
            return;

        lines.Add("Inner exception:");
        lines.Add($"  {InnerException.GetType().FullName}: {InnerException.Message}");
    }
}
