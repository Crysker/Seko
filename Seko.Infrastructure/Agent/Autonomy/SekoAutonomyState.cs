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
    bool RequiresVerification)
{
    public bool RequiresProjectExplanationEvidence { get; init; }
}

public sealed record SekoAutonomyState
{
    public SekoAutonomyPhase Phase { get; init; } =
        SekoAutonomyPhase.Planning;

    public bool WorkspaceModificationAllowed { get; init; }

    public bool ProjectExplanationEvidenceRequired { get; init; }

    public int TotalModelRounds { get; init; }

    public int PhaseModelRounds { get; init; }

    public int ConsecutiveNoProgressRounds { get; init; }

    public int RepairCycles { get; init; }

    public bool ResearchCompleted { get; init; }

    public bool WorkspaceEvidenceObserved { get; init; }

    public bool ProjectInventoryObserved { get; init; }

    public int ProjectInventoryDirectoryCount { get; init; }

    public IReadOnlyList<string> ProjectInventoryFiles { get; init; } =
        Array.Empty<string>();

    public IReadOnlyList<string> InspectedWorkspaceFiles { get; init; } =
        Array.Empty<string>();

    public IReadOnlyList<string> ProjectExplanationRecoveryCandidates { get; init; } =
        Array.Empty<string>();

    public int ModificationGeneration { get; init; }

    public string? LatestModificationPath { get; init; }

    public bool LatestModificationRequiresBuild { get; init; } =
        true;

    public int VerifiedModificationGeneration { get; init; } =
        -1;

    public string? LastVerificationFailureSignature { get; init; }

    public int LastVerificationFailureGeneration { get; init; } =
        -1;

    public bool IsTerminal =>
        Phase is SekoAutonomyPhase.Complete
            or SekoAutonomyPhase.Incomplete;
}