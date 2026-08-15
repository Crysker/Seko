namespace Seko.Infrastructure.Agent;

public enum SekoExecutionPhase
{
    Conversation,
    Research,
    DirectWebFetch,
    WorkspaceInspection,
    WorkspaceModification,
    Verification,
    Synthesis
}

public sealed record SekoToolSelectionPlan(
    SekoExecutionPhase Phase,
    IReadOnlyCollection<string> ToolNames)
{
    public bool Allows(
        string toolName)
    {
        return
            !string.IsNullOrWhiteSpace(
                toolName)
            && ToolNames.Contains(
                toolName,
                StringComparer.Ordinal);
    }
}
