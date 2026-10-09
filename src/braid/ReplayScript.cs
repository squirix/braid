namespace Braid;

/// <summary>Holds the scripted steps of a replay run and the steps appended when the run completes in fork order.</summary>
internal sealed class ReplayScript
{
    private readonly List<ReplayStep> _steps;

    internal ReplayScript(IReadOnlyList<ReplayStep> steps, bool completesInForkOrder)
    {
        _steps = [.. steps];
        CompletesInForkOrder = completesInForkOrder;
    }

    /// <summary>Gets a value indicating whether workers still waiting after the last step are released in fork order instead of failing the run.</summary>
    internal bool CompletesInForkOrder { get; }

    /// <summary>Gets the scripted steps followed by any appended completion steps.</summary>
    internal IReadOnlyList<ReplayStep> Steps => _steps;

    /// <summary>Appends a completion step, so the reported schedule reproduces the whole run.</summary>
    /// <param name="step">The step to append.</param>
    internal void AppendCompletionStep(in ReplayStep step) => _steps.Add(step);
}
