namespace Seko.Core.Workspaces;

public interface IWorkspaceStore
{
    WorkspaceState Load();

    void Save(WorkspaceState state);
}