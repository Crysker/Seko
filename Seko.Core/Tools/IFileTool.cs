using Seko.Core.Workspaces;

namespace Seko.Core.Tools;

public interface IFileTool
{
    Task<string> ReadTextAsync(
        Workspace workspace,
        string relativePath,
        CancellationToken cancellationToken = default);

    Task WriteTextAsync(
        Workspace workspace,
        string relativePath,
        string content,
        CancellationToken cancellationToken = default);

    bool Exists(
        Workspace workspace,
        string relativePath);
}