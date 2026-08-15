namespace Seko.Infrastructure.Agent;

public sealed record SekoAutonomyToolPlan(
    SekoAutonomyPhase Phase,
    IReadOnlyCollection<string> ToolNames,
    string Reason)
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