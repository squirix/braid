using System.Diagnostics.CodeAnalysis;

namespace Braid.Internal;

internal static class BraidScheduleTextParser
{
    public static bool TryParse(
        string? text,
        [NotNullWhen(true)] out BraidSchedule? schedule,
        [NotNullWhen(false)] out string? error)
    {
        schedule = null;
        error = null;

        if (text is null)
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
        {
            var line = lines[lineIndex].Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line[0] == '#')
            {
                continue;
            }

            var lineNumber = lineIndex + 1;
            var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

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

            switch (tokens.Length)
            {
                case 1:
                    error = $"Line {lineNumber}: Missing worker id and probe name.";
                    return false;
                case 2:
                    error = $"Line {lineNumber}: Missing probe name.";
                    return false;
            }

            var workerId = tokens[1];
            var probeName = tokens[2];

            if (workerId.Length == 0)
            {
                error = $"Line {lineNumber}: Worker id must not be empty.";
                return false;
            }

            if (probeName.Length == 0)
            {
                error = $"Line {lineNumber}: Probe name must not be empty.";
                return false;
            }

            BraidStep step;
            switch (kind)
            {
                case BraidStepKind.Hit:
                    step = BraidStep.Hit(workerId, probeName);
                    break;
                case BraidStepKind.Arrive:
                    step = BraidStep.Arrive(workerId, probeName);
                    break;
                case BraidStepKind.Release:
                    step = BraidStep.Release(workerId, probeName);
                    break;
                default:
                    error = $"Line {lineNumber}: Unknown braid step kind '{kind}'.";
                    return false;
            }

            steps.Add(step);
        }

        if (steps.Count == 0)
        {
            error = "Text contains no replay steps (only comments or empty lines).";
            return false;
        }

        try
        {
            schedule = BraidSchedule.Replay([.. steps]);
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            return false;
        }

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
}
