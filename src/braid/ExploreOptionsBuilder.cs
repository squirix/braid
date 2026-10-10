namespace Braid;

/// <summary>Configures bounded exploration options.</summary>
public sealed class ExploreOptionsBuilder
{
    private TimeSpan _timeout = TimeSpan.FromSeconds(10);
    private int _seed = Environment.TickCount;
    private int _maxSchedules = 1_000;
    private int _maxStepsPerSchedule = 100;

    /// <summary>Builds the configured options.</summary>
    /// <returns>The configured exploration options.</returns>
    public ExploreOptions Build() => new(_seed, _maxSchedules, _maxStepsPerSchedule, _timeout);

    /// <summary>Sets the seed reported with a failure. Schedules run in a fixed order, so the seed does not change them.</summary>
    /// <param name="seed">The seed value.</param>
    /// <returns>The current builder.</returns>
    public ExploreOptionsBuilder WithSeed(int seed)
    {
        _seed = seed;
        return this;
    }

    /// <summary>Sets the maximum number of schedules to run.</summary>
    /// <param name="maxSchedules">The schedule cap.</param>
    /// <returns>The current builder.</returns>
    public ExploreOptionsBuilder WithMaxSchedules(int maxSchedules)
    {
        _maxSchedules = maxSchedules;
        return this;
    }

    /// <summary>Sets the number of hit steps of a schedule whose order is explored; start steps do not count.</summary>
    /// <remarks>Only the first steps are explored. After them a run releases the worker that has waited longest, so the test can run to completion.</remarks>
    /// <param name="maxStepsPerSchedule">The per-schedule step cap.</param>
    /// <returns>The current builder.</returns>
    public ExploreOptionsBuilder WithMaxStepsPerSchedule(int maxStepsPerSchedule)
    {
        _maxStepsPerSchedule = maxStepsPerSchedule;
        return this;
    }

    /// <summary>Sets the exploration timeout.</summary>
    /// <param name="timeout">The timeout value.</param>
    /// <returns>The current builder.</returns>
    public ExploreOptionsBuilder WithTimeout(TimeSpan timeout)
    {
        _timeout = timeout;
        return this;
    }
}
