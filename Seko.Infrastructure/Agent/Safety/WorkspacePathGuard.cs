using System.Text.RegularExpressions;

namespace Seko.Infrastructure.Agent.Safety;

public sealed class WorkspacePathGuard
{
    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".cs",
            ".xaml",
            ".csproj",
            ".sln",
            ".json",
            ".jsonc",
            ".xml",
            ".config",
            ".toml",
            ".ini",
            ".props",
            ".targets",
            ".md",
            ".txt",
            ".yml",
            ".yaml",
            ".html",
            ".css",
            ".js",
            ".ts"
        };

    private static readonly HashSet<string> IgnoredDirectories =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".git",
            ".vs",
            "bin",
            "obj",
            "node_modules"
        };

    private readonly string _workspaceRoot;

    public WorkspacePathGuard(
        string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(
                workspaceRoot))
        {
            throw new ArgumentException(
                "Workspace root cannot be empty.",
                nameof(workspaceRoot));
        }

        _workspaceRoot =
            Path.GetFullPath(
                workspaceRoot);
    }

    public string WorkspaceRoot =>
        _workspaceRoot;

    public string ResolveSafePath(
        string? relativePath)
    {
        relativePath ??=
            string.Empty;

        if (Path.IsPathRooted(
                relativePath))
        {
            throw new UnauthorizedAccessException(
                "Absolute paths are not allowed.");
        }

        var fullPath =
            Path.GetFullPath(
                Path.Combine(
                    _workspaceRoot,
                    relativePath));

        var root =
            _workspaceRoot.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

        if (string.Equals(
                fullPath,
                root,
                StringComparison.OrdinalIgnoreCase))
        {
            return fullPath;
        }

        var rootPrefix =
            root
            + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(
                rootPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException(
                "Path escapes the active workspace.");
        }

        if (PathContainsIgnoredDirectory(
                Path.GetRelativePath(
                    root,
                    fullPath)))
        {
            throw new UnauthorizedAccessException(
                "Access to Git internals, build output and generated directories is blocked.");
        }

        EnsureNoReparsePointEscape(
            root,
            fullPath);

        return fullPath;
    }

    public void EnsureAllowedFile(
        string fullPath)
    {
        if (IsSensitiveFile(
                fullPath))
        {
            throw new UnauthorizedAccessException(
                "This file is treated as sensitive and cannot be accessed by Seko.");
        }

        var fileName =
            Path.GetFileName(
                fullPath);

        if (fileName.Equals(
                ".gitignore",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var extension =
            Path.GetExtension(
                fullPath);

        if (!AllowedExtensions.Contains(
                extension))
        {
            throw new InvalidOperationException(
                $"File type '{extension}' is not enabled for Seko yet.");
        }
    }

    public void EnsureSourceModificationBelongsToProject(
        string fullPath)
    {
        var extension =
            Path.GetExtension(
                fullPath);

        if (!File.Exists(
                fullPath)
            && (extension.Equals(
                    ".csproj",
                    StringComparison.OrdinalIgnoreCase)
                || extension.Equals(
                    ".sln",
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "NEW_PROJECT_FILE_REQUIRES_EXPLICIT_SUPPORT.\n\n"
                + "The generic write_file tool cannot create a new .csproj or .sln because an unreferenced project can make a build pass while the new code is never compiled. "
                + "Add dedicated project-creation support before allowing this operation.");
        }

        if (!extension.Equals(
                ".cs",
                StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(
                ".xaml",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var projectRoots =
            GetKnownProjectRoots();

        if (projectRoots.Any(
                projectRoot =>
                    IsPathInsideDirectory(
                        fullPath,
                        projectRoot)))
        {
            return;
        }

        var relativePath =
            NormalizeRelativePath(
                Path.GetRelativePath(
                    _workspaceRoot,
                    fullPath));

        var knownRoots =
            projectRoots.Count == 0
                ? "- No .NET project roots could be discovered."
                : string.Join(
                    Environment.NewLine,
                    projectRoots.Select(
                        projectRoot =>
                            "- "
                            + NormalizeRelativePath(
                                Path.GetRelativePath(
                                    _workspaceRoot,
                                    projectRoot))));

        throw new InvalidOperationException(
            "SOURCE_PATH_NOT_IN_PROJECT.\n\n"
            + $"Refusing to modify source file '{relativePath}' because it is not inside a real .NET project included by the active solution.\n\n"
            + "Known project roots:\n"
            + knownRoots
            + "\n\nInspect the solution/project structure and modify source only inside the appropriate project instead of using an orphan or invented workspace folder.");
    }

    public bool IsSensitiveFile(
        string fullPath)
    {
        var fileName =
            Path.GetFileName(
                fullPath);

        var extension =
            Path.GetExtension(
                fullPath);

        return
            fileName.Equals(
                ".env",
                StringComparison.OrdinalIgnoreCase)

            || fileName.StartsWith(
                ".env.",
                StringComparison.OrdinalIgnoreCase)

            || fileName.Equals(
                "secrets.json",
                StringComparison.OrdinalIgnoreCase)

            || fileName.Equals(
                "credentials.json",
                StringComparison.OrdinalIgnoreCase)

            || fileName.Equals(
                "appsettings.Local.json",
                StringComparison.OrdinalIgnoreCase)

            || fileName.Equals(
                "appsettings.Development.local.json",
                StringComparison.OrdinalIgnoreCase)

            || extension.Equals(
                ".pem",
                StringComparison.OrdinalIgnoreCase)

            || extension.Equals(
                ".pfx",
                StringComparison.OrdinalIgnoreCase)

            || extension.Equals(
                ".p12",
                StringComparison.OrdinalIgnoreCase)

            || extension.Equals(
                ".key",
                StringComparison.OrdinalIgnoreCase);
    }

    public bool IsSearchableFile(
        string file)
    {
        var fileName =
            Path.GetFileName(
                file);

        if (fileName.Equals(
                ".gitignore",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return
            AllowedExtensions.Contains(
                Path.GetExtension(
                    file));
    }

    public bool IsIgnoredDirectory(
        string directoryName)
    {
        return
            IgnoredDirectories.Contains(
                directoryName);
    }

    public IEnumerable<string> EnumerateWorkspaceFiles(
        int maximumFiles)
    {
        if (maximumFiles <= 0)
        {
            yield break;
        }

        var queue =
            new Queue<string>();

        queue.Enqueue(
            _workspaceRoot);

        var yielded =
            0;

        while (queue.Count > 0
               && yielded < maximumFiles)
        {
            var current =
                queue.Dequeue();

            foreach (var directory
                     in GetDirectoriesSafe(current)
                         .OrderBy(
                             path => path,
                             StringComparer.OrdinalIgnoreCase))
            {
                var directoryName =
                    Path.GetFileName(
                        directory);

                if (IsIgnoredDirectory(
                        directoryName))
                {
                    continue;
                }

                queue.Enqueue(
                    directory);
            }

            foreach (var file
                     in GetFilesSafe(current)
                         .OrderBy(
                             path => path,
                             StringComparer.OrdinalIgnoreCase))
            {
                yield return file;

                yielded++;

                if (yielded
                    >= maximumFiles)
                {
                    yield break;
                }
            }
        }
    }

    public IEnumerable<string> GetDirectoriesSafe(
        string path)
    {
        try
        {
            return
                Directory.GetDirectories(
                        path)
                    .Where(
                        directory =>
                            !IsReparsePoint(
                                directory))
                    .ToArray();
        }
        catch
        {
            return
                Array.Empty<string>();
        }
    }

    public IEnumerable<string> GetFilesSafe(
        string path)
    {
        try
        {
            return
                Directory.GetFiles(
                        path)
                    .Where(
                        file =>
                            !IsReparsePoint(
                                file))
                    .ToArray();
        }
        catch
        {
            return
                Array.Empty<string>();
        }
    }

    public bool IsReparsePoint(
        string path)
    {
        try
        {
            return
                (File.GetAttributes(
                     path)
                 & FileAttributes.ReparsePoint)
                != 0;
        }
        catch
        {
            // If metadata cannot be inspected, do not traverse it implicitly.
            return true;
        }
    }

    private void EnsureNoReparsePointEscape(
        string workspaceRoot,
        string fullPath)
    {
        var relativePath =
            Path.GetRelativePath(
                workspaceRoot,
                fullPath);

        var current =
            workspaceRoot;

        foreach (var part
                 in relativePath.Split(
                     new[]
                     {
                         Path.DirectorySeparatorChar,
                         Path.AltDirectorySeparatorChar
                     },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current =
                Path.Combine(
                    current,
                    part);

            if (!File.Exists(
                    current)
                && !Directory.Exists(
                    current))
            {
                // Remaining path components do not exist yet.
                break;
            }

            FileAttributes attributes;

            try
            {
                attributes =
                    File.GetAttributes(
                        current);
            }
            catch (Exception exception)
            {
                throw new UnauthorizedAccessException(
                    "Seko could not safely verify the requested workspace path.",
                    exception);
            }

            if ((attributes
                 & FileAttributes.ReparsePoint)
                != 0)
            {
                throw new UnauthorizedAccessException(
                    "Access through symbolic links, junctions or other reparse points is blocked because the target could escape the active workspace.");
            }
        }
    }

    private IReadOnlyList<string> GetKnownProjectRoots()
    {
        var roots =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        var solutionFiles =
            Directory
                .EnumerateFiles(
                    _workspaceRoot,
                    "*.sln",
                    SearchOption.TopDirectoryOnly)
                .Where(
                    path =>
                        !IsReparsePoint(
                            path))
                .OrderBy(
                    path => path,
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

        foreach (var solutionFile
                 in solutionFiles)
        {
            try
            {
                foreach (var line
                         in File.ReadLines(
                             solutionFile))
                {
                    foreach (Match match
                             in Regex.Matches(
                                 line,
                                 @"[""']([^""']+\.csproj)[""']",
                                 RegexOptions.IgnoreCase))
                    {
                        var projectRelativePath =
                            match.Groups[1]
                                .Value
                                .Replace(
                                    '\\',
                                    Path.DirectorySeparatorChar)
                                .Replace(
                                    '/',
                                    Path.DirectorySeparatorChar);

                        var projectPath =
                            Path.GetFullPath(
                                Path.Combine(
                                    _workspaceRoot,
                                    projectRelativePath));

                        if (!IsPathInsideDirectory(
                                projectPath,
                                _workspaceRoot)
                            || !File.Exists(
                                projectPath))
                        {
                            continue;
                        }

                        var projectRoot =
                            Path.GetDirectoryName(
                                projectPath);

                        if (!string.IsNullOrWhiteSpace(
                                projectRoot))
                        {
                            roots.Add(
                                projectRoot);
                        }
                    }
                }
            }
            catch
            {
                // Fall back to project-file discovery below.
            }
        }

        if (roots.Count == 0)
        {
            try
            {
                foreach (var projectPath
                         in EnumerateWorkspaceFiles(
                             10_000)
                             .Where(
                                 path =>
                                     Path.GetExtension(
                                             path)
                                         .Equals(
                                             ".csproj",
                                             StringComparison.OrdinalIgnoreCase)))
                {
                    var projectRoot =
                        Path.GetDirectoryName(
                            projectPath);

                    if (!string.IsNullOrWhiteSpace(
                            projectRoot))
                    {
                        roots.Add(
                            Path.GetFullPath(
                                projectRoot));
                    }
                }
            }
            catch
            {
                // The caller will report that no project roots were discovered.
            }
        }

        return roots
            .OrderBy(
                path => path,
                StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsPathInsideDirectory(
        string fullPath,
        string directory)
    {
        var normalizedPath =
            Path.GetFullPath(
                fullPath);

        var normalizedDirectory =
            Path.GetFullPath(
                directory)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

        if (string.Equals(
                normalizedPath,
                normalizedDirectory,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var directoryPrefix =
            normalizedDirectory
            + Path.DirectorySeparatorChar;

        return
            normalizedPath.StartsWith(
                directoryPrefix,
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathContainsIgnoredDirectory(
        string path)
    {
        var parts =
            path.Split(
                new[]
                {
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar
                },
                StringSplitOptions.RemoveEmptyEntries);

        return
            parts.Any(
                part =>
                    IgnoredDirectories.Contains(
                        part));
    }

    private static string NormalizeRelativePath(
        string path)
    {
        return
            path.Replace(
                '\\',
                '/');
    }
}
