namespace Braid;

/// <summary>Identifies whether a <see cref="RunException" /> came from braid infrastructure, user test code, or a run that timed out.</summary>
public enum RunFailureOrigin
{
    /// <summary>
    /// Scheduler or runner infrastructure failure, including a timeout while a worker ran and others were parked, which braid's ordering may cause.
    /// See <see cref="Timeout" />.
    /// </summary>
    Scheduler = 0,

    /// <summary>Failure from user test code in the run callback or a forked worker.</summary>
    UserTest = 1,

    /// <summary>
    /// The run did not complete within <see cref="RunOptions.Timeout" /> or <see cref="ExploreOptions.Timeout" />, for example because a worker hung,
    /// and no running worker could be waiting for a parked one. A timeout while a worker ran and others were parked is reported with <see cref="Scheduler" />.
    /// </summary>
    Timeout = 2,
}
