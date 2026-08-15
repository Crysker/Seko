namespace Seko.Infrastructure.Agent;

public enum SekoAutonomyDisposition
{
    Continue,
    Complete,
    Incomplete
}

public enum SekoAutonomySignal
{
    MeaningfulProgress,
    NoProgress,
    ResearchCompleted,
    WorkspaceEvidenceObserved,
    InspectionCompleted,
    ModificationCompleted,
    VerificationSucceeded,
    VerificationFailed,
    RepairCompleted,
    SynthesisCompleted
}

public sealed record SekoAutonomyDecision(
    SekoAutonomyState State,
    SekoAutonomyDisposition Disposition,
    string Reason)
{
    public bool CanContinue =>
        Disposition == SekoAutonomyDisposition.Continue;
}