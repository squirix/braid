namespace Braid;

/// <summary>
/// Represents a failure discovered during a braid run with reproducibility details.
/// Inner exceptions are preserved on the base <see cref="Exception" /> and summarized in <see cref="ToString" />.
/// </summary>
public sealed class BraidRunException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BraidRunException" /> class.
    /// </summary>
    /// <param name="message">The exception message.</param>
    /// <param name="seed">The seed used for the failing iteration.</param>
    /// <param name="iteration">The failing iteration index.</param>
    /// <param name="trace">The recorded scheduling trace.</param>
    /// <param name="schedule">The configured replay schedule.</param>
    /// <param name="innerException">The underlying exception.</param>
    public BraidRunException(string message, int seed, int iteration, IReadOnlyList<string> trace, IReadOnlyList<BraidStep>? schedule, Exception? innerException)
        : base(message, innerException)
    {
        ArgumentNullException.ThrowIfNull(trace);

        Seed = seed;
        Iteration = iteration;
        Trace = Array.AsReadOnly(trace.ToArray());
        Schedule = schedule is null ? Array.Empty<BraidStep>() : Array.AsReadOnly(schedule.ToArray());
    }

    /// <summary>
    /// Gets the zero-based failing iteration index.
    /// </summary>
    public int Iteration { get; }

    /// <summary>
    /// Gets the configured replay schedule, or an empty list when random scheduling was used.
    /// </summary>
    public IReadOnlyList<BraidStep> Schedule { get; }

    /// <summary>
    /// Gets the seed used for the failing iteration.
    /// </summary>
    public int Seed { get; }

    /// <summary>
    /// Gets the recorded scheduling trace for the failing iteration.
    /// </summary>
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
                lines.Add(step.Kind == BraidStepKind.Hit ? $"  {index + 1}. {step.WorkerId} @ {step.ProbeName}" : $"  {index + 1}. {step.Kind} {step.WorkerId} @ {step.ProbeName}");
            }

            lines.Add("Replay text:");
            try
            {
                var replaySchedule = BraidSchedule.Replay([.. Schedule]);
                var replayText = replaySchedule.ToReplayText();
                if (replayText.Length > 0)
                {
                    foreach (var segment in replayText.Split(Environment.NewLine, StringSplitOptions.None))
                    {
                        lines.Add(segment);
                    }
                }
            }
            catch (InvalidOperationException)
            {
                lines.Add("Replay text unavailable: schedule contains values that cannot be represented in replay text.");
            }
        }

        lines.Add("Trace:");
        for (var index = 0; index < Trace.Count; index++)
        {
            lines.Add($"  {index + 1}. {Trace[index]}");
        }

        if (InnerException is null)
            return string.Join(Environment.NewLine, lines);
        lines.Add("Inner exception:");
        lines.Add($"  {InnerException.GetType().FullName}: {InnerException.Message}");
        return string.Join(Environment.NewLine, lines);
    }
}
