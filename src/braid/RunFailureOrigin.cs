namespace Braid;

/// <summary>Identifies whether a <see cref="RunException" /> came from braid infrastructure, user test code, or a run that timed out.</summary>
public enum RunFailureOrigin
{
    /// <summary>Scheduler or runner infrastructure failure.</summary>
    Scheduler = 0,

    /// <summary>Failure from user test code in the run callback or a forked worker.</summary>
    UserTest = 1,

    /// <summary>
    /// The run did not complete within <see cref="RunOptions.Timeout" /> or <see cref="ExploreOptions.Timeout" /> while no worker was parked at a probe,
    /// for example because a worker hung. A timeout while workers were parked at probes is reported with <see cref="Scheduler" />,
    /// because the running worker may be waiting for a parked one.
    /// </summary>
    Timeout = 2,
}
