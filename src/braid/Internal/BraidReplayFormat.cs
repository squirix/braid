namespace Braid.Internal;

internal static class BraidReplayFormat
{
    internal static string CanonicalStepLine(BraidStep step) =>
        step.Kind switch
        {
            BraidStepKind.Hit => $"hit {step.WorkerId} {step.ProbeName}",
            BraidStepKind.Arrive => $"arrive {step.WorkerId} {step.ProbeName}",
            BraidStepKind.Release => $"release {step.WorkerId} {step.ProbeName}",
            _ => $"{step.Kind} {step.WorkerId} {step.ProbeName}",
        };
}
