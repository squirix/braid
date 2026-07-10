namespace Braid;

/// <summary>Identifies whether a <see cref="BraidRunException" /> came from braid infrastructure or user test code.</summary>
public enum BraidRunFailureOrigin
{
    /// <summary>Scheduler or runner infrastructure failure.</summary>
    Scheduler = 0,

    /// <summary>Failure from user test code in the run callback or a forked worker.</summary>
    UserTest = 1,
}
