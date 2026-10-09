namespace Braid;

/// <summary>Identifies whether a <see cref="RunException" /> came from braid infrastructure, user test code, or a run that timed out.</summary>
public enum RunFailureOrigin
{
    /// <summary>Scheduler or runner infrastructure failure.</summary>
    Scheduler = 0,

    /// <summary>Failure from user test code in the run callback or a forked worker.</summary>
    UserTest = 1,

    /// <summary>The run did not complete within <see cref="RunOptions.Timeout" />, for example because workers deadlocked or a worker hung.</summary>
    Timeout = 2,
}
