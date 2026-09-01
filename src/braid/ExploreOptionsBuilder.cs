namespace Braid;

/// <summary>Configures bounded exploration options.</summary>
public sealed class ExploreOptionsBuilder
{
    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);
    private int _seed = Environment.TickCount;
    private int _maxSchedules = 1_000;
    private int _maxStepsPerSchedule = 100;

    /// <summary>Builds the configured options.</summary>
    /// <returns>The configured exploration options.</returns>
    public ExploreOptions Build() => new(_seed, _maxSchedules, _maxStepsPerSchedule, _timeout);

    /// <summary>Sets the base seed used for discovery and replay runs.</summary>
    /// <param name="seed">The seed value.</param>
    /// <returns>The current builder.</returns>
    public ExploreOptionsBuilder WithSeed(int seed)
    {
        _seed = seed;
        return this;
    }

    /// <summary>Sets the maximum number of distinct replay schedules to try.</summary>
    /// <param name="maxSchedules">The schedule cap.</param>
    /// <returns>The current builder.</returns>
    public ExploreOptionsBuilder WithMaxSchedules(int maxSchedules)
    {
        _maxSchedules = maxSchedules;
        return this;
    }

    /// <summary>Sets the maximum number of hit steps per generated replay schedule.</summary>
    /// <param name="maxStepsPerSchedule">The per-schedule step cap.</param>
    /// <returns>The current builder.</returns>
    public ExploreOptionsBuilder WithMaxStepsPerSchedule(int maxStepsPerSchedule)
    {
        _maxStepsPerSchedule = maxStepsPerSchedule;
        return this;
    }
}
