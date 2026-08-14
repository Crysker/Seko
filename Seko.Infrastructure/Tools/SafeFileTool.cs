using Seko.Core.Audit;
using Seko.Core.Tools;
using Seko.Core.Workspaces;

namespace Seko.Infrastructure.Tools;

public sealed class SafeFileTool : IFileTool
{
    private readonly IAuditLog _auditLog;

    public SafeFileTool(IAuditLog auditLog)
    {
        _auditLog = auditLog;
    }

    public async Task<string> ReadTextAsync(
        Workspace workspace,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        var fullPath = ResolveSafePath(workspace, relativePath);

        try
        {
            var content = await File.ReadAllTextAsync(
                fullPath,
                cancellationToken);

            _auditLog.Add(new AuditEntry
            {
                Action = "File.Read",
                Description = fullPath,
                Success = true
            });

            return content;
        }
        catch
        {
            _auditLog.Add(new AuditEntry
            {
                Action = "File.Read",
                Description = fullPath,
                Success = false
            });

            throw;
        }
    }

    public async Task WriteTextAsync(
        Workspace workspace,
        string relativePath,
        string content,
        CancellationToken cancellationToken = default)
    {
        var fullPath = ResolveSafePath(workspace, relativePath);

        try
        {
            var directory = Path.GetDirectoryName(fullPath);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(
                fullPath,
                content,
                cancellationToken);

            _auditLog.Add(new AuditEntry
            {
                Action = "File.Write",
                Description = fullPath,
                Success = true
            });
        }
        catch
        {
            _auditLog.Add(new AuditEntry
            {
                Action = "File.Write",
                Description = fullPath,
                Success = false
            });

            throw;
        }
    }

    public bool Exists(
        Workspace workspace,
        string relativePath)
    {
        var fullPath = ResolveSafePath(workspace, relativePath);

        return File.Exists(fullPath);
    }

    private static string ResolveSafePath(
        Workspace workspace,
        string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException(
                "A relative path is required.",
                nameof(relativePath));
        }

        var workspaceRoot = Path.GetFullPath(workspace.RootPath);

        var combinedPath = Path.Combine(
            workspaceRoot,
            relativePath);

        var fullPath = Path.GetFullPath(combinedPath);

        var normalizedRoot = workspaceRoot.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(
                normalizedRoot,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException(
                "Seko blocked access because the requested path is outside the active workspace.");
        }

        return fullPath;
    }
}