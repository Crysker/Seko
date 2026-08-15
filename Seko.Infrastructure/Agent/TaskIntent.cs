namespace Seko.Infrastructure.Agent;

public sealed record TaskIntent(
    bool RequiresWorkspaceTools,
    bool RequiresModification,
    bool ExplicitBuildRequested)
{
    public bool ExecutionSuppressed { get; init; }

    public bool IsWorkspaceCapabilityQuestion { get; init; }

    public bool RequiresProjectExplanationEvidence { get; init; }

    public bool RequiresProductIdentityUpdate { get; init; }

    public string? ExpectedCurrentProductVersion { get; init; }

    public string? RequestedProductVersion { get; init; }

    public string? RequestedProductDisplayName { get; init; }
}