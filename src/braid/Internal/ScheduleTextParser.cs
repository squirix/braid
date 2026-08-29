using System.Diagnostics.CodeAnalysis;

namespace Braid.Internal;

internal static class ScheduleTextParser
{
    public static bool TryParse(string? text, [NotNullWhen(true)] out BraidSchedule? schedule, [NotNullWhen(false)] out string? error)
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

        var steps = new List<BraidStep>();
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            if (!TryParseLine(lines[lineIndex], lineIndex + 1, steps, out error))
                return false;

        if (steps.Count != 0)
            return TryCreateSchedule(steps, out schedule, out error);

        error = "Text contains no replay steps (only comments or empty lines).";
        return false;
    }

    private static bool TryCreateSchedule(List<BraidStep> steps, out BraidSchedule? schedule, [NotNullWhen(false)] out string? error)
    {
        schedule = null;
        error = null;

        try
        {
            schedule = BraidSchedule.Replay(steps);
            return true;
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool TryCreateStep(BraidStepKind kind, string workerId, string probeName, int lineNumber, out BraidStep step, [NotNullWhen(false)] out string? error)
    {
        error = null;
        switch (kind)
        {
            case BraidStepKind.Hit:
                step = BraidStep.Hit(workerId, probeName);
                return true;
            case BraidStepKind.Arrive:
                step = BraidStep.Arrive(workerId, probeName);
                return true;
            case BraidStepKind.Release:
                step = BraidStep.Release(workerId, probeName);
                return true;
            default:
                step = default;
                error = $"Line {lineNumber}: Unknown braid step kind '{kind}'.";
                return false;
        }
    }

    private static bool TryParseLine(string rawLine, int lineNumber, List<BraidStep> steps, [NotNullWhen(false)] out string? error)
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

    private static bool TryParseOperation(string token, out BraidStepKind kind)
    {
        if (token.Equals("hit", StringComparison.OrdinalIgnoreCase))
        {
            kind = BraidStepKind.Hit;
            return true;
        }

        if (token.Equals("arrive", StringComparison.OrdinalIgnoreCase))
        {
            kind = BraidStepKind.Arrive;
            return true;
        }

        if (token.Equals("release", StringComparison.OrdinalIgnoreCase))
        {
            kind = BraidStepKind.Release;
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
            default:
                error = $"Line {lineNumber}: Expected exactly 3 tokens (operation, worker id, probe name); found {tokens.Length}.";
                return false;
        }

        if (workerId.Length == 0)
        {
            error = $"Line {lineNumber}: Worker id must not be empty.";
            return false;
        }

        if (probeName.Length != 0)
            return true;

        error = $"Line {lineNumber}: Probe name must not be empty.";
        return false;
    }
}
