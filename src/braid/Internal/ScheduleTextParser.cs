using System.Diagnostics.CodeAnalysis;

namespace Braid.Internal;

internal static class ScheduleTextParser
{
    public static bool TryParse(string? text, [NotNullWhen(true)] out ReplaySchedule? schedule, [NotNullWhen(false)] out string? error)
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
            return TryCreateSchedule(steps, out schedule, out error);

        error = "Text contains no replay steps (only comments or empty lines).";
        return false;
    }

    private static bool TryCreateSchedule(List<ReplayStep> steps, out ReplaySchedule? schedule, [NotNullWhen(false)] out string? error)
    {
        schedule = null;
        error = null;

        try
        {
            schedule = ReplaySchedule.Replay(steps);
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
