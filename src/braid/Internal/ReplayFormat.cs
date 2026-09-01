namespace Braid.Internal;

internal static class ReplayFormat
{
    internal static string CanonicalStepLine(ReplayStep step) => step.Kind switch
    {
        ReplayStepKind.Hit => $"hit {step.WorkerId} {step.ProbeName}",
        ReplayStepKind.Arrive => $"arrive {step.WorkerId} {step.ProbeName}",
        ReplayStepKind.Release => $"release {step.WorkerId} {step.ProbeName}",
        _ => $"{step.Kind} {step.WorkerId} {step.ProbeName}",
    };
}
