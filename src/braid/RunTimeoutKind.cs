namespace Braid;

/// <summary>Describes the scheduler state associated with a run timeout.</summary>
public enum RunTimeoutKind
{
    /// <summary>The failure was not recorded as a run timeout.</summary>
    None = 0,

    /// <summary>The run callback had not started a join when the timeout elapsed.</summary>
    BeforeJoin = 1,

    /// <summary>A running worker may have been waiting for a worker parked by the scheduler.</summary>
    PossibleParkedWorkerDependency = 2,

    /// <summary>The snapshot showed no running worker paired with a parked worker.</summary>
    NoParkedWorkerDependency = 3,
}
