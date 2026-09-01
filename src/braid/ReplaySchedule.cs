using System.Diagnostics.CodeAnalysis;
using System.Text;
using Braid.Attributes;

namespace Braid;

/// <summary>Represents a typed replay schedule for a braid run.</summary>
[Immutable]
public sealed class ReplaySchedule
{
    private ReplaySchedule(IReadOnlyList<ReplayStep> steps)
    {
        Steps = steps;
    }

    /// <summary>Gets the replay steps in order.</summary>
    public IReadOnlyList<ReplayStep> Steps { get; }

    /// <summary>Parses a line-based textual replay schedule. Operation names are case-insensitive; worker ids and probe names are case-sensitive.</summary>
    /// <param name="text">The schedule text. Empty lines and full-line # comments are ignored. At least one step is required.</param>
    /// <returns>A replay schedule.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text" /> is null.</exception>
    /// <exception cref="FormatException">The text is not a valid schedule.</exception>
    public static ReplaySchedule Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return TryParseScheduleText(text, out var schedule, out var error) ? schedule : throw new FormatException(error);
    }

    /// <summary>Creates a replay schedule from the supplied steps. When the list is non-empty, the run must consume every step in order.</summary>
    /// <param name="steps">The worker replay steps.</param>
    /// <returns>A replay schedule.</returns>
    public static ReplaySchedule Replay(params ReplayStep[] steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        return CreateReplaySchedule(steps, steps.Length);
    }

    /// <summary>Creates a replay schedule from the supplied steps. When the list is non-empty, the run must consume every step in order.</summary>
    /// <param name="steps">The worker replay steps.</param>
    /// <returns>A replay schedule.</returns>
    public static ReplaySchedule Replay(IReadOnlyList<ReplayStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        return CreateReplaySchedule(steps, steps.Count);
    }

    /// <summary>Attempts to parse a line-based textual replay schedule.</summary>
    /// <param name="text">The schedule text.</param>
    /// <param name="schedule">The parsed schedule when this method returns <see langword="true" />.</param>
    /// <param name="error">A diagnostic message when this method returns <see langword="false" />.</param>
    /// <returns><see langword="true" /> if parsing succeeded; otherwise <see langword="false" />.</returns>
    public static bool TryParse(string? text, [NotNullWhen(true)] out ReplaySchedule? schedule, [NotNullWhen(false)] out string? error) =>
        TryParseScheduleText(text, out schedule, out error);

    /// <summary>
    /// Returns a canonical line-based replay schedule using lower-case operation names and <see cref="Environment.NewLine" /> between steps.
    /// The format matches <see cref="Parse(string)" /> for non-empty results. An empty schedule yields <see cref="string.Empty" />, which <see cref="Parse(string)" /> does not accept.
    /// </summary>
    /// <returns>Replay text, or <see cref="string.Empty" /> when there are no steps.</returns>
    /// <exception cref="InvalidOperationException">A worker id or probe name contains whitespace and cannot be represented in this format.</exception>
    public string ToReplayText()
    {
        if (Steps.Count == 0)
            return string.Empty;

        var builder = new StringBuilder();
        for (var index = 0; index < Steps.Count; index++)
        {
            if (index > 0)
                _ = builder.Append(Environment.NewLine);

            var step = Steps[index];
            EnsureReplayTextRepresentable(step.WorkerId, true);
            EnsureReplayTextRepresentable(step.ProbeName, false);

            var operation = step.Kind switch
            {
                ReplayStepKind.Hit => "hit",
                ReplayStepKind.Arrive => "arrive",
                ReplayStepKind.Release => "release",
                _ => throw new InvalidOperationException($"Braid step kind '{step.Kind}' cannot be exported to replay text."),
            };

            _ = builder.Append(operation).Append(' ').Append(step.WorkerId).Append(' ').Append(step.ProbeName);
        }

        return builder.ToString();
    }

    internal void Validate()
    {
        for (var index = 0; index < Steps.Count; index++)
            Steps[index].Validate();
    }

    private static ReplaySchedule CreateReplaySchedule(IReadOnlyList<ReplayStep> steps, int count)
    {
        var copy = new ReplayStep[count];
        for (var index = 0; index < count; index++)
        {
            copy[index] = steps[index];
            copy[index].Validate();
        }

        return new ReplaySchedule(Array.AsReadOnly(copy));
    }

    private static void EnsureReplayTextRepresentable(string value, bool isWorkerId)
    {
        foreach (var ch in value)
        {
            if (!char.IsWhiteSpace(ch))
                continue;
            throw new InvalidOperationException(
                isWorkerId ? "Worker id cannot be exported to replay text because it contains whitespace."
                    : "Probe name cannot be exported to replay text because it contains whitespace.");
        }
    }

    private static bool TryParseScheduleText(string? text, [NotNullWhen(true)] out ReplaySchedule? schedule, [NotNullWhen(false)] out string? error)
    {
        schedule = null;
        error = null;

        if (text == null)
        {
            error = "Text must not be null.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Text is empty or contains only whitespace.";
            return false;
        }

        var steps = new List<ReplayStep>();
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            if (!TryParseLine(lines[lineIndex], lineIndex + 1, steps, out error))
                return false;
        }

        if (steps.Count != 0)
            return TryCreateScheduleFromParsed(steps, out schedule, out error);

        error = "Text contains no replay steps (only comments or empty lines).";
        return false;
    }

    private static bool TryCreateScheduleFromParsed(List<ReplayStep> steps, out ReplaySchedule? schedule, [NotNullWhen(false)] out string? error)
    {
        schedule = null;
        error = null;

        try
        {
            schedule = Replay(steps);
            return true;
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool TryCreateStep(ReplayStepKind kind, string workerId, string probeName, int lineNumber, out ReplayStep step, [NotNullWhen(false)] out string? error)
    {
        error = null;
        switch (kind)
        {
            case ReplayStepKind.Hit:
                step = ReplayStep.Hit(workerId, probeName);
                return true;
            case ReplayStepKind.Arrive:
                step = ReplayStep.Arrive(workerId, probeName);
                return true;
            case ReplayStepKind.Release:
                step = ReplayStep.Release(workerId, probeName);
                return true;
            default:
                step = default;
                error = $"Line {lineNumber}: Unknown braid step kind '{kind}'.";
                return false;
        }
    }

    private static bool TryParseLine(string rawLine, int lineNumber, List<ReplayStep> steps, [NotNullWhen(false)] out string? error)
    {
        error = null;
        var line = rawLine.Trim();
        if (line.Length == 0 || line[0] == '#')
            return true;

        char[]? separators = null;
        var tokens = line.Split(separators, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length > 3)
        {
            error = $"Line {lineNumber}: Expected exactly 3 tokens (operation, worker id, probe name); found {tokens.Length}.";
            return false;
        }

        if (!TryParseOperation(tokens[0], out var kind))
        {
            error = $"Line {lineNumber}: Unknown operation '{tokens[0]}'. Expected 'hit', 'arrive', or 'release'.";
            return false;
        }

        if (!TryParseWorkerAndProbe(tokens, lineNumber, out var workerId, out var probeName, out error))
            return false;

        if (!TryCreateStep(kind, workerId, probeName, lineNumber, out var step, out error))
            return false;

        steps.Add(step);
        return true;
    }

    private static bool TryParseOperation(string token, out ReplayStepKind kind)
    {
        if (token.Equals("hit", StringComparison.OrdinalIgnoreCase))
        {
            kind = ReplayStepKind.Hit;
            return true;
        }

        if (token.Equals("arrive", StringComparison.OrdinalIgnoreCase))
        {
            kind = ReplayStepKind.Arrive;
            return true;
        }

        if (token.Equals("release", StringComparison.OrdinalIgnoreCase))
        {
            kind = ReplayStepKind.Release;
            return true;
        }

        kind = default;
        return false;
    }

    private static bool TryParseWorkerAndProbe(string[] tokens, int lineNumber, out string workerId, out string probeName, [NotNullWhen(false)] out string? error)
    {
        error = null;
        workerId = string.Empty;
        probeName = string.Empty;

        switch (tokens.Length)
        {
            case 1:
                error = $"Line {lineNumber}: Missing worker id and probe name.";
                return false;
            case 2:
                error = $"Line {lineNumber}: Missing probe name.";
                return false;
            case 3:
                workerId = tokens[1];
                probeName = tokens[2];
                break;
        }

        return true;
    }
}
