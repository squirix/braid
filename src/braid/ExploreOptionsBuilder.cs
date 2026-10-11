namespace Braid;

/// <summary>Configures bounded exploration options.</summary>
public sealed class ExploreOptionsBuilder
{
    private TimeSpan _timeout = TimeSpan.FromSeconds(10);
    private int _maxSchedules = 1_000;
    private int _maxStepsPerSchedule = 100;

    /// <summary>Builds the configured options.</summary>
    /// <returns>The configured exploration options.</returns>
    public ExploreOptions Build() => new(_maxSchedules, _maxStepsPerSchedule, _timeout);

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
