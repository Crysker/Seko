namespace Seko.Infrastructure.Agent;

public sealed record TaskIntent(
    bool RequiresWorkspaceTools,
    bool RequiresModification,
    bool ExplicitBuildRequested)
{
    public bool RequiresProjectExplanationEvidence { get; init; }
}