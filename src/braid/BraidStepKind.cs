namespace Braid;

/// <summary>
/// Represents scripted replay behavior for a schedule step.
/// </summary>
public enum BraidStepKind
{
    /// <summary>
    /// Wait for a worker to be blocked at the probe and then release it.
    /// </summary>
    Hit = 0,

    /// <summary>
    /// Wait for a worker to be blocked at the probe and keep it held.
    /// </summary>
    Arrive = 1,

    /// <summary>
    /// Release a worker previously held by an <see cref="Arrive" /> step.
    /// </summary>
    Release = 2,
}
