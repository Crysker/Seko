namespace Seko.Core.Workspaces;

public sealed class WorkspaceState
{
    public List<Workspace> Workspaces { get; init; } = new();

    public Guid? ActiveWorkspaceId { get; set; }
}