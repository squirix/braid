using Braid.Attributes;

namespace Braid;

/// <summary>Defines seed, iteration, timeout, and replay options for a braid run.</summary>
[Immutable]
public sealed class RunOptions
{
    /// <summary>Gets the longest supported run timeout, about 49.7 days (the limit of a <see cref="CancellationTokenSource" /> delay).</summary>
    public static TimeSpan MaxTimeout { get; } = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    /// <summary>Gets the default options.</summary>
    public static RunOptions Default { get; } = new();

    /// <summary>Gets or initializes the number of scheduling iterations to run.</summary>
    public int Iterations { get; init; } = 100;

    /// <summary>Gets or initializes an optional typed schedule used to replay a specific interleaving.</summary>
    public ReplaySchedule? Schedule { get; init; }

    /// <summary>Gets or initializes the base seed. Each iteration adds its zero-based index to this seed.</summary>
    public int? Seed { get; init; }

    /// <summary>Gets or initializes the per-iteration timeout, counted from the start of the run callback. It must be positive and at most <see cref="MaxTimeout" />.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets the steps a run of an exploration replays before it chooses the waiting worker itself, or <see langword="null" /> for any other run.
    /// The first run of an exploration replays no steps.
    /// </summary>
    internal IReadOnlyList<ReplayStep>? ExploredPrefix { get; init; }

    /// <summary>Throws when <paramref name="value" /> is not a positive timeout of at most <see cref="MaxTimeout" />.</summary>
    /// <param name="value">The timeout to check.</param>
    /// <param name="paramName">The name reported in the exception.</param>
    /// <exception cref="ArgumentOutOfRangeException">The timeout is not positive or exceeds <see cref="MaxTimeout" />.</exception>
    internal static void ValidateTimeout(TimeSpan value, string paramName)
    {
        if (value <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(paramName, value, "Timeout must be positive.");

        if (value > MaxTimeout)
            throw new ArgumentOutOfRangeException(paramName, value, $"Timeout must be at most {MaxTimeout}.");
    }

    internal void Validate()
    {
        ValidatePositive(Iterations, nameof(Iterations), "Iterations must be positive.");
        ValidateTimeout(Timeout, nameof(Timeout));
        Schedule?.Validate();
    }

    private static void ValidatePositive(int value, string paramName, string message)
    {
        if (value <= 0)
            throw new ArgumentOutOfRangeException(paramName, value, message);
    }
}
