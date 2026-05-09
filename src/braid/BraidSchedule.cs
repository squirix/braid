using System.Diagnostics.CodeAnalysis;
using Braid.Internal;

namespace Braid;

/// <summary>
/// Represents a typed replay schedule for a braid run.
/// </summary>
public sealed class BraidSchedule
{
    private BraidSchedule(IReadOnlyList<BraidStep> steps)
    {
        Steps = steps;
    }

    /// <summary>
    /// Gets the replay steps in order.
    /// </summary>
    public IReadOnlyList<BraidStep> Steps { get; }

    /// <summary>
    /// Creates a replay schedule from the supplied steps. When the list is non-empty, the run must consume every step in order.
    /// </summary>
    /// <param name="steps">The worker replay steps.</param>
    /// <returns>A replay schedule.</returns>
    public static BraidSchedule Replay(params BraidStep[] steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        var copy = new BraidStep[steps.Length];
        for (var index = 0; index < steps.Length; index++)
        {
            copy[index] = steps[index];
            copy[index].Validate();
        }

        return new BraidSchedule(Array.AsReadOnly(copy));
    }

    /// <summary>
    /// Parses a line-based textual replay schedule. Operation names are case-insensitive; worker ids and probe names are case-sensitive.
    /// </summary>
    /// <param name="text">The schedule text. Empty lines and full-line <c>#</c> comments are ignored. At least one step is required.</param>
    /// <returns>A replay schedule.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    /// <exception cref="FormatException">The text is not a valid schedule.</exception>
    public static BraidSchedule Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return !BraidScheduleTextParser.TryParse(text, out var schedule, out var error) ? throw new FormatException(error) : schedule;
    }

    /// <summary>
    /// Attempts to parse a line-based textual replay schedule.
    /// </summary>
    /// <param name="text">The schedule text.</param>
    /// <param name="schedule">The parsed schedule when this method returns <see langword="true"/>.</param>
    /// <param name="error">A diagnostic message when this method returns <see langword="false"/>.</param>
    /// <returns><see langword="true"/> if parsing succeeded; otherwise <see langword="false"/>.</returns>
    public static bool TryParse(
        string? text,
        [NotNullWhen(true)] out BraidSchedule? schedule,
        [NotNullWhen(false)] out string? error)
        => BraidScheduleTextParser.TryParse(text, out schedule, out error);

    internal void Validate()
    {
        foreach (var step in Steps)
        {
            step.Validate();
        }
    }
}
