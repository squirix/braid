namespace Braid;

/// <summary>
/// Represents a deterministic failure discovered during a Braid run.
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
    /// <param name="innerException">The underlying exception.</param>
    public BraidRunException(string message, int seed, int iteration, IReadOnlyList<string> trace, Exception? innerException)
        : base(FormatMessage(message, seed, iteration, trace), innerException)
    {
        Seed = seed;
        Iteration = iteration;
        Trace = trace;
    }

    /// <summary>
    /// Gets the failing iteration index.
    /// </summary>
    public int Iteration { get; }

    /// <summary>
    /// Gets the seed used for the failing iteration.
    /// </summary>
    public int Seed { get; }

    /// <summary>
    /// Gets the recorded scheduling trace.
    /// </summary>
    public IReadOnlyList<string> Trace { get; }

    private static string FormatMessage(string message, int seed, int iteration, IReadOnlyList<string> trace)
    {
        var lines = new List<string>
        {
            message,
            $"Seed: {seed}",
            $"Iteration: {iteration}",
            "Trace:",
        };

        lines.AddRange(trace.Select(static line => "  " + line));
        return string.Join(Environment.NewLine, lines);
    }
}
