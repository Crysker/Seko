namespace Seko.Infrastructure.Agent;

public enum SekoAutonomyPhase
{
    Planning,
    Research,
    Inspection,
    Action,
    Verification,
    Repair,
    Synthesis,
    Complete,
    Incomplete
}

public sealed record SekoAutonomyTaskRequirements(
    bool RequiresResearch,
    bool RequiresWorkspaceInspection,
    bool RequiresModification,
    bool RequiresVerification);

public sealed record SekoAutonomyState
{
    public SekoAutonomyPhase Phase { get; init; } =
        SekoAutonomyPhase.Planning;

    public int TotalModelRounds { get; init; }

    public int PhaseModelRounds { get; init; }

    public int ConsecutiveNoProgressRounds { get; init; }

    public int RepairCycles { get; init; }

    public bool ResearchCompleted { get; init; }

    public bool WorkspaceEvidenceObserved { get; init; }

    public int ModificationGeneration { get; init; }

    public int VerifiedModificationGeneration { get; init; } =
        -1;

    public string? LastVerificationFailureSignature { get; init; }

    public int LastVerificationFailureGeneration { get; init; } =
        -1;

    public bool IsTerminal =>
        Phase is SekoAutonomyPhase.Complete
            or SekoAutonomyPhase.Incomplete;
}