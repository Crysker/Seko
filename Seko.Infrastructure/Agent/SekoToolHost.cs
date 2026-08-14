using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Seko.Core.Workspaces;

namespace Seko.Infrastructure.Agent;

public sealed class SekoToolHost
{
    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".cs",
            ".xaml",
            ".csproj",
            ".sln",
            ".json",
            ".xml",
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

    private readonly Workspace _workspace;
    private readonly string _workspaceRoot;

    private readonly HashSet<string> _changedFiles =
        new(StringComparer.OrdinalIgnoreCase);

    private bool _isGitRepository;
    private bool _baselineClean = true;

    private bool _buildWasRun;
    private bool _lastBuildSucceeded;

    public SekoToolHost(
        Workspace workspace)
    {
        _workspace =
            workspace;

        _workspaceRoot =
            Path.GetFullPath(
                workspace.RootPath);
    }

    public async Task BeginTaskAsync(
        CancellationToken cancellationToken = default)
    {
        _changedFiles.Clear();

        _buildWasRun =
            false;

        _lastBuildSucceeded =
            false;

        var gitCheck =
            await RunProcessAsync(
                "git",
                new[]
                {
                    "rev-parse",
                    "--is-inside-work-tree"
                },
                _workspaceRoot,
                cancellationToken);

        _isGitRepository =
            gitCheck.ExitCode == 0
            && gitCheck.Output.Trim().Equals(
                "true",
                StringComparison.OrdinalIgnoreCase);

        if (!_isGitRepository)
        {
            _baselineClean =
                true;

            return;
        }

        var status =
            await RunProcessAsync(
                "git",
                new[]
                {
                    "status",
                    "--porcelain"
                },
                _workspaceRoot,
                cancellationToken);

        _baselineClean =
            status.ExitCode == 0
            && string.IsNullOrWhiteSpace(
                status.Output);
    }

    public JsonArray CreateToolDefinitions()
    {
        return new JsonArray
        {
            CreateFunctionTool(
                "find_files",
                "Find files by file name inside the active workspace. Prefer this when you know a file name but not its relative path.",
                new JsonObject
                {
                    ["type"] =
                        "object",

                    ["properties"] =
                        new JsonObject
                        {
                            ["name"] =
                                StringProperty(
                                    "File name or part of a file name to find, for example MainWindow.xaml.")
                        },

                    ["required"] =
                        new JsonArray
                        {
                            "name"
                        }
                }),

            CreateFunctionTool(
                "find_text",
                "Find text inside one known file and return only the matching lines plus nearby context. Prefer this over read_file for focused edits.",
                new JsonObject
                {
                    ["type"] =
                        "object",

                    ["properties"] =
                        new JsonObject
                        {
                            ["path"] =
                                StringProperty(
                                    "File path relative to the workspace root."),

                            ["text"] =
                                StringProperty(
                                    "Text to search for inside the file."),

                            ["context_lines"] =
                                new JsonObject
                                {
                                    ["type"] =
                                        "integer",

                                    ["description"] =
                                        "Number of surrounding lines to return. Usually 3 to 6.",

                                    ["minimum"] =
                                        0,

                                    ["maximum"] =
                                        10
                                }
                        },

                    ["required"] =
                        new JsonArray
                        {
                            "path",
                            "text"
                        }
                }),

            CreateFunctionTool(
                "list_files",
                "List files and directories inside a specific workspace directory. Use this for directory overviews, not for locating one known filename.",
                new JsonObject
                {
                    ["type"] =
                        "object",

                    ["properties"] =
                        new JsonObject
                        {
                            ["path"] =
                                StringProperty(
                                    "Path relative to the workspace root. Use an empty string for the root."),

                            ["recursive"] =
                                new JsonObject
                                {
                                    ["type"] =
                                        "boolean",

                                    ["description"] =
                                        "Whether child directories should be listed recursively."
                                }
                        },

                    ["required"] =
                        new JsonArray
                        {
                            "path",
                            "recursive"
                        }
                }),

            CreateFunctionTool(
                "read_file",
                "Read an entire text or source-code file. Use find_text instead when only a small relevant section is needed.",
                new JsonObject
                {
                    ["type"] =
                        "object",

                    ["properties"] =
                        new JsonObject
                        {
                            ["path"] =
                                StringProperty(
                                    "File path relative to the workspace root.")
                        },

                    ["required"] =
                        new JsonArray
                        {
                            "path"
                        }
                }),

            CreateFunctionTool(
                "write_file",
                "Create a new source/text file or deliberately replace an entire existing file.",
                new JsonObject
                {
                    ["type"] =
                        "object",

                    ["properties"] =
                        new JsonObject
                        {
                            ["path"] =
                                StringProperty(
                                    "File path relative to the workspace root."),

                            ["content"] =
                                StringProperty(
                                    "The complete finished contents of the file.")
                        },

                    ["required"] =
                        new JsonArray
                        {
                            "path",
                            "content"
                        }
                }),

            CreateFunctionTool(
                "replace_text",
                "Replace exactly one matching section in an existing source file. Prefer this for focused edits.",
                new JsonObject
                {
                    ["type"] =
                        "object",

                    ["properties"] =
                        new JsonObject
                        {
                            ["path"] =
                                StringProperty(
                                    "File path relative to the workspace root."),

                            ["old_text"] =
                                StringProperty(
                                    "Exact existing text to replace. It must occur exactly once."),

                            ["new_text"] =
                                StringProperty(
                                    "Replacement text.")
                        },

                    ["required"] =
                        new JsonArray
                        {
                            "path",
                            "old_text",
                            "new_text"
                        }
                }),

            CreateFunctionTool(
                "build_project",
                "Run dotnet build for the active .NET workspace.",
                EmptyParameters()),

            CreateFunctionTool(
                "git_status",
                "Inspect Git status for the active workspace.",
                EmptyParameters()),

            CreateFunctionTool(
                "git_diff",
                "Show the current Git diff for the active workspace.",
                EmptyParameters())
        };
    }

    public async Task<string> ExecuteAsync(
        string toolName,
        string argumentsJson,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var document =
                JsonDocument.Parse(
                    string.IsNullOrWhiteSpace(
                        argumentsJson)
                        ? "{}"
                        : argumentsJson);

            var arguments =
                document.RootElement;

            return toolName switch
            {
                "find_files" =>
                    FindFiles(
                        arguments),

                "find_text" =>
                    await FindTextAsync(
                        arguments,
                        cancellationToken),

                "list_files" =>
                    ListFiles(
                        arguments),

                "read_file" =>
                    await ReadFileAsync(
                        arguments,
                        cancellationToken),

                "write_file" =>
                    await WriteFileAsync(
                        arguments,
                        cancellationToken),

                "replace_text" =>
                    await ReplaceTextAsync(
                        arguments,
                        cancellationToken),

                "build_project" =>
                    await BuildProjectAsync(
                        cancellationToken),

                "git_status" =>
                    await GetGitStatusAsync(
                        cancellationToken),

                "git_diff" =>
                    await GetGitDiffAsync(
                        cancellationToken),

                _ =>
                    $"ERROR: Unknown tool '{toolName}'."
            };
        }
        catch (Exception exception)
        {
            return
                $"ERROR: {exception.GetType().Name}: " +
                exception.Message;
        }
    }

    public async Task<string?> TryAutoCommitAsync(
        string userRequest,
        CancellationToken cancellationToken = default)
    {
        if (_changedFiles.Count == 0)
        {
            return null;
        }

        if (!_isGitRepository)
        {
            return
                "Git: files changed, but this workspace is not a Git repository.";
        }

        if (!_baselineClean)
        {
            return
                "Git: automatic commit skipped because the repository already contained uncommitted changes before this task began.";
        }

        var requiresBuild =
            _changedFiles.Any(
                RequiresBuild);

        if (requiresBuild
            && (!_buildWasRun
                || !_lastBuildSucceeded))
        {
            return
                "Git: changes were not committed because a successful build has not been verified.";
        }

        var filesToStage =
            _changedFiles
                .OrderBy(
                    path => path,
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

        if (filesToStage.Count == 0)
        {
            return null;
        }

        var addArguments =
            new List<string>
            {
                "add",
                "--"
            };

        addArguments.AddRange(
            filesToStage);

        var addResult =
            await RunProcessAsync(
                "git",
                addArguments,
                _workspaceRoot,
                cancellationToken);

        if (addResult.ExitCode != 0)
        {
            return
                "Git: staging failed.\n\n" +
                addResult.Output;
        }

        var stagedDiff =
            await RunProcessAsync(
                "git",
                new[]
                {
                    "diff",
                    "--cached",
                    "--name-only"
                },
                _workspaceRoot,
                cancellationToken);

        if (string.IsNullOrWhiteSpace(
                stagedDiff.Output))
        {
            return
                "Git: there were no effective changes to commit.";
        }

        var commitMessage =
            CreateCommitMessage(
                userRequest);

        var commitResult =
            await RunProcessAsync(
                "git",
                new[]
                {
                    "commit",
                    "-m",
                    commitMessage
                },
                _workspaceRoot,
                cancellationToken);

        if (commitResult.ExitCode != 0)
        {
            return
                "Git: changes were staged, but the commit failed.\n\n" +
                commitResult.Output;
        }

        var hashResult =
            await RunProcessAsync(
                "git",
                new[]
                {
                    "rev-parse",
                    "--short",
                    "HEAD"
                },
                _workspaceRoot,
                cancellationToken);

        return
            $"Git: committed locally as " +
            $"{hashResult.Output.Trim()} — " +
            commitMessage;
    }

    private string FindFiles(
        JsonElement arguments)
    {
        var query =
            GetString(
                arguments,
                "name")
            .Trim();

        if (string.IsNullOrWhiteSpace(
                query))
        {
            return
                "ERROR: File name cannot be empty.";
        }

        const int maximumResults =
            50;

        var results =
            new List<string>();

        var queue =
            new Queue<string>();

        queue.Enqueue(
            _workspaceRoot);

        while (queue.Count > 0
               && results.Count < maximumResults)
        {
            var current =
                queue.Dequeue();

            foreach (
                var directory
                in GetDirectoriesSafe(current)
                    .OrderBy(
                        path => path,
                        StringComparer.OrdinalIgnoreCase))
            {
                var directoryName =
                    Path.GetFileName(
                        directory);

                if (IgnoredDirectories.Contains(
                        directoryName))
                {
                    continue;
                }

                queue.Enqueue(
                    directory);
            }

            foreach (
                var file
                in GetFilesSafe(current)
                    .OrderBy(
                        path => path,
                        StringComparer.OrdinalIgnoreCase))
            {
                if (IsSensitiveFile(
                        file))
                {
                    continue;
                }

                var fileName =
                    Path.GetFileName(
                        file);

                if (!fileName.Contains(
                        query,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                results.Add(
                    ToRelativePath(
                        file));

                if (results.Count >= maximumResults)
                {
                    break;
                }
            }
        }

        if (results.Count == 0)
        {
            return
                $"No accessible files matching '{query}' were found.";
        }

        return
            string.Join(
                Environment.NewLine,
                results);
    }

    private async Task<string> FindTextAsync(
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var relativePath =
            GetString(
                arguments,
                "path");

        var searchText =
            GetString(
                arguments,
                "text");

        if (string.IsNullOrEmpty(
                searchText))
        {
            return
                "ERROR: Search text cannot be empty.";
        }

        var contextLines =
            GetOptionalInteger(
                arguments,
                "context_lines",
                4);

        contextLines =
            Math.Clamp(
                contextLines,
                0,
                10);

        var fullPath =
            ResolveSafePath(
                relativePath);

        EnsureAllowedFile(
            fullPath);

        if (!File.Exists(
                fullPath))
        {
            return
                $"ERROR: File not found: {relativePath}";
        }

        var fileInfo =
            new FileInfo(
                fullPath);

        if (fileInfo.Length > 600_000)
        {
            return
                "ERROR: File is too large for the current text search tool.";
        }

        var lines =
            await File.ReadAllLinesAsync(
                fullPath,
                cancellationToken);

        var matchingLines =
            new List<int>();

        for (var index = 0;
             index < lines.Length;
             index++)
        {
            if (lines[index].Contains(
                    searchText,
                    StringComparison.OrdinalIgnoreCase))
            {
                matchingLines.Add(
                    index);

                if (matchingLines.Count >= 12)
                {
                    break;
                }
            }
        }

        if (matchingLines.Count == 0)
        {
            return
                $"Text '{searchText}' was not found in {NormalizeRelativePath(relativePath)}.";
        }

        var builder =
            new StringBuilder();

        builder.AppendLine(
            $"FILE: {NormalizeRelativePath(relativePath)}");

        builder.AppendLine(
            $"SEARCH: {searchText}");

        builder.AppendLine();

        foreach (var matchIndex in matchingLines)
        {
            var start =
                Math.Max(
                    0,
                    matchIndex - contextLines);

            var end =
                Math.Min(
                    lines.Length - 1,
                    matchIndex + contextLines);

            builder.AppendLine(
                $"--- Match at line {matchIndex + 1} ---");

            for (var index = start;
                 index <= end;
                 index++)
            {
                var marker =
                    index == matchIndex
                        ? ">"
                        : " ";

                builder.Append(
                    marker);

                builder.Append(
                    (index + 1)
                    .ToString()
                    .PadLeft(5));

                builder.Append(
                    " | ");

                builder.AppendLine(
                    lines[index]);
            }

            builder.AppendLine();
        }

        return builder
            .ToString()
            .TrimEnd();
    }

    private string ListFiles(
        JsonElement arguments)
    {
        var relativePath =
            GetString(
                arguments,
                "path");

        var recursive =
            GetBoolean(
                arguments,
                "recursive");

        var directory =
            ResolveSafePath(
                relativePath);

        if (!Directory.Exists(
                directory))
        {
            return
                $"ERROR: Directory not found: {relativePath}";
        }

        const int maximumEntries =
            300;

        var results =
            new List<string>();

        var queue =
            new Queue<string>();

        queue.Enqueue(
            directory);

        while (queue.Count > 0
               && results.Count < maximumEntries)
        {
            var current =
                queue.Dequeue();

            foreach (
                var childDirectory
                in GetDirectoriesSafe(current)
                    .OrderBy(
                        path => path,
                        StringComparer.OrdinalIgnoreCase))
            {
                var directoryName =
                    Path.GetFileName(
                        childDirectory);

                if (IgnoredDirectories.Contains(
                        directoryName))
                {
                    continue;
                }

                results.Add(
                    "[DIR] " +
                    ToRelativePath(
                        childDirectory));

                if (recursive)
                {
                    queue.Enqueue(
                        childDirectory);
                }

                if (results.Count >= maximumEntries)
                {
                    break;
                }
            }

            if (results.Count >= maximumEntries)
            {
                break;
            }

            foreach (
                var file
                in GetFilesSafe(current)
                    .OrderBy(
                        path => path,
                        StringComparer.OrdinalIgnoreCase))
            {
                if (IsSensitiveFile(
                        file))
                {
                    continue;
                }

                results.Add(
                    "[FILE] " +
                    ToRelativePath(
                        file));

                if (results.Count >= maximumEntries)
                {
                    break;
                }
            }

            if (!recursive)
            {
                break;
            }
        }

        if (results.Count == 0)
        {
            return
                "No accessible files found.";
        }

        return
            string.Join(
                Environment.NewLine,
                results);
    }

    private async Task<string> ReadFileAsync(
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var relativePath =
            GetString(
                arguments,
                "path");

        var fullPath =
            ResolveSafePath(
                relativePath);

        EnsureAllowedFile(
            fullPath);

        if (!File.Exists(
                fullPath))
        {
            return
                $"ERROR: File not found: {relativePath}";
        }

        var fileInfo =
            new FileInfo(
                fullPath);

        if (fileInfo.Length > 600_000)
        {
            return
                "ERROR: File is too large for the current read tool.";
        }

        var content =
            await File.ReadAllTextAsync(
                fullPath,
                cancellationToken);

        return
            $"FILE: {NormalizeRelativePath(relativePath)}\n\n" +
            content;
    }

    private async Task<string> WriteFileAsync(
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        EnsureModificationAllowed();

        var relativePath =
            GetString(
                arguments,
                "path");

        var content =
            GetString(
                arguments,
                "content");

        if (content.Length > 1_000_000)
        {
            return
                "ERROR: Refusing to write more than 1,000,000 characters at once.";
        }

        var fullPath =
            ResolveSafePath(
                relativePath);

        EnsureAllowedFile(
            fullPath);

        var directory =
            Path.GetDirectoryName(
                fullPath);

        if (!string.IsNullOrWhiteSpace(
                directory))
        {
            Directory.CreateDirectory(
                directory);
        }

        await File.WriteAllTextAsync(
            fullPath,
            content,
            new UTF8Encoding(false),
            cancellationToken);

        var normalizedPath =
            NormalizeRelativePath(
                Path.GetRelativePath(
                    _workspaceRoot,
                    fullPath));

        _changedFiles.Add(
            normalizedPath);

        return
            $"Wrote {normalizedPath}.";
    }

    private async Task<string> ReplaceTextAsync(
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        EnsureModificationAllowed();

        var relativePath =
            GetString(
                arguments,
                "path");

        var oldText =
            GetString(
                arguments,
                "old_text");

        var newText =
            GetString(
                arguments,
                "new_text");

        if (string.IsNullOrEmpty(
                oldText))
        {
            return
                "ERROR: old_text cannot be empty.";
        }

        var fullPath =
            ResolveSafePath(
                relativePath);

        EnsureAllowedFile(
            fullPath);

        if (!File.Exists(
                fullPath))
        {
            return
                $"ERROR: File not found: {relativePath}";
        }

        var content =
            await File.ReadAllTextAsync(
                fullPath,
                cancellationToken);

        var occurrences =
            CountOccurrences(
                content,
                oldText);

        if (occurrences == 0)
        {
            return
                "ERROR: old_text was not found. Use find_text or read_file before editing.";
        }

        if (occurrences > 1)
        {
            return
                $"ERROR: old_text appears {occurrences} times. Use a more specific section.";
        }

        var updatedContent =
            content.Replace(
                oldText,
                newText,
                StringComparison.Ordinal);

        await File.WriteAllTextAsync(
            fullPath,
            updatedContent,
            new UTF8Encoding(false),
            cancellationToken);

        var normalizedPath =
            NormalizeRelativePath(
                Path.GetRelativePath(
                    _workspaceRoot,
                    fullPath));

        _changedFiles.Add(
            normalizedPath);

        return
            $"Updated {normalizedPath}.";
    }

    private async Task<string> BuildProjectAsync(
        CancellationToken cancellationToken)
    {
        _buildWasRun =
            true;

        _lastBuildSucceeded =
            false;

        var target =
            FindBuildTarget();

        if (target is null)
        {
            return
                "ERROR: No .sln or .csproj file was found in this workspace.";
        }

        var localAppData =
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);

        var buildOutput =
            Path.Combine(
                localAppData,
                "Seko",
                "BuildCheck",
                _workspace.Id.ToString("N"),
                DateTime.Now.ToString(
                    "yyyyMMdd-HHmmssfff"));

        Directory.CreateDirectory(
            buildOutput);

        var result =
            await RunProcessAsync(
                "dotnet",
                new[]
                {
                    "build",
                    target,
                    "--no-restore",
                    $"-p:BaseOutputPath={buildOutput}{Path.DirectorySeparatorChar}"
                },
                _workspaceRoot,
                cancellationToken,
                TimeSpan.FromMinutes(3));

        _lastBuildSucceeded =
            result.ExitCode == 0;

        return
            $"BUILD EXIT CODE: {result.ExitCode}\n\n" +
            TrimOutput(
                result.Output,
                30_000);
    }

    private async Task<string> GetGitStatusAsync(
        CancellationToken cancellationToken)
    {
        if (!_isGitRepository)
        {
            return
                "This workspace is not a Git repository.";
        }

        var result =
            await RunProcessAsync(
                "git",
                new[]
                {
                    "status",
                    "--short"
                },
                _workspaceRoot,
                cancellationToken);

        var currentStatus =
            string.IsNullOrWhiteSpace(
                result.Output)
                ? "Working tree clean."
                : result.Output;

        return
            $"Working tree was clean when this task began: {_baselineClean}\n\n" +
            currentStatus;
    }

    private async Task<string> GetGitDiffAsync(
        CancellationToken cancellationToken)
    {
        if (!_isGitRepository)
        {
            return
                "This workspace is not a Git repository.";
        }

        var result =
            await RunProcessAsync(
                "git",
                new[]
                {
                    "diff"
                },
                _workspaceRoot,
                cancellationToken);

        if (string.IsNullOrWhiteSpace(
                result.Output))
        {
            return
                "No unstaged Git diff.";
        }

        return
            TrimOutput(
                result.Output,
                30_000);
    }

    private void EnsureModificationAllowed()
    {
        if (_isGitRepository
            && !_baselineClean)
        {
            throw new InvalidOperationException(
                "The Git repository already contained uncommitted changes before this task began. " +
                "Seko will not modify files until those changes are committed or reverted.");
        }
    }

    private string ResolveSafePath(
        string relativePath)
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
            root +
            Path.DirectorySeparatorChar;

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

        return fullPath;
    }

    private static void EnsureAllowedFile(
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

    private static bool IsSensitiveFile(
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

    private string? FindBuildTarget()
    {
        var solution =
            Directory
                .EnumerateFiles(
                    _workspaceRoot,
                    "*.sln",
                    SearchOption.TopDirectoryOnly)
                .FirstOrDefault();

        if (solution is not null)
        {
            return solution;
        }

        return Directory
            .EnumerateFiles(
                _workspaceRoot,
                "*.csproj",
                SearchOption.AllDirectories)
            .FirstOrDefault(
                path =>
                    !PathContainsIgnoredDirectory(
                        Path.GetRelativePath(
                            _workspaceRoot,
                            path)));
    }

    private string ToRelativePath(
        string fullPath)
    {
        return
            NormalizeRelativePath(
                Path.GetRelativePath(
                    _workspaceRoot,
                    fullPath));
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

    private static string GetString(
        JsonElement arguments,
        string propertyName)
    {
        if (!arguments.TryGetProperty(
                propertyName,
                out var element)
            || element.ValueKind != JsonValueKind.String)
        {
            throw new ArgumentException(
                $"Missing string argument '{propertyName}'.");
        }

        return
            element.GetString()
            ?? string.Empty;
    }

    private static bool GetBoolean(
        JsonElement arguments,
        string propertyName)
    {
        if (!arguments.TryGetProperty(
                propertyName,
                out var element))
        {
            throw new ArgumentException(
                $"Missing boolean argument '{propertyName}'.");
        }

        if (element.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.False)
        {
            return false;
        }

        throw new ArgumentException(
            $"Argument '{propertyName}' must be true or false.");
    }

    private static int GetOptionalInteger(
        JsonElement arguments,
        string propertyName,
        int defaultValue)
    {
        if (!arguments.TryGetProperty(
                propertyName,
                out var element))
        {
            return defaultValue;
        }

        if (element.ValueKind == JsonValueKind.Number
            && element.TryGetInt32(
                out var value))
        {
            return value;
        }

        return defaultValue;
    }

    private static int CountOccurrences(
        string text,
        string value)
    {
        var count =
            0;

        var index =
            0;

        while (true)
        {
            index =
                text.IndexOf(
                    value,
                    index,
                    StringComparison.Ordinal);

            if (index < 0)
            {
                return count;
            }

            count++;

            index +=
                value.Length;
        }
    }

    private static bool RequiresBuild(
        string relativePath)
    {
        var extension =
            Path.GetExtension(
                relativePath);

        return
            extension.Equals(
                ".cs",
                StringComparison.OrdinalIgnoreCase)

            || extension.Equals(
                ".xaml",
                StringComparison.OrdinalIgnoreCase)

            || extension.Equals(
                ".csproj",
                StringComparison.OrdinalIgnoreCase)

            || extension.Equals(
                ".sln",
                StringComparison.OrdinalIgnoreCase)

            || extension.Equals(
                ".props",
                StringComparison.OrdinalIgnoreCase)

            || extension.Equals(
                ".targets",
                StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateCommitMessage(
        string userRequest)
    {
        var firstLine =
            userRequest
                .Split(
                    new[]
                    {
                        '\r',
                        '\n'
                    },
                    StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault()
                ?.Trim()
            ?? "self improvement";

        firstLine =
            firstLine.Replace(
                "\"",
                string.Empty);

        if (firstLine.Length > 60)
        {
            firstLine =
                firstLine[..60].TrimEnd()
                + "...";
        }

        return
            $"Seko: {firstLine}";
    }

    private static IEnumerable<string> GetDirectoriesSafe(
        string path)
    {
        try
        {
            return
                Directory.GetDirectories(
                    path);
        }
        catch
        {
            return
                Array.Empty<string>();
        }
    }

    private static IEnumerable<string> GetFilesSafe(
        string path)
    {
        try
        {
            return
                Directory.GetFiles(
                    path);
        }
        catch
        {
            return
                Array.Empty<string>();
        }
    }

    private static JsonObject StringProperty(
        string description)
    {
        return
            new JsonObject
            {
                ["type"] =
                    "string",

                ["description"] =
                    description
            };
    }

    private static JsonObject EmptyParameters()
    {
        return
            new JsonObject
            {
                ["type"] =
                    "object",

                ["properties"] =
                    new JsonObject(),

                ["required"] =
                    new JsonArray()
            };
    }

    private static JsonObject CreateFunctionTool(
        string name,
        string description,
        JsonObject parameters)
    {
        return
            new JsonObject
            {
                ["type"] =
                    "function",

                ["function"] =
                    new JsonObject
                    {
                        ["name"] =
                            name,

                        ["description"] =
                            description,

                        ["parameters"] =
                            parameters
                    }
            };
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string executable,
        IEnumerable<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        var startInfo =
            new ProcessStartInfo
            {
                FileName =
                    executable,

                WorkingDirectory =
                    workingDirectory,

                RedirectStandardOutput =
                    true,

                RedirectStandardError =
                    true,

                UseShellExecute =
                    false,

                CreateNoWindow =
                    true
            };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(
                argument);
        }

        try
        {
            using var process =
                new Process
                {
                    StartInfo =
                        startInfo
                };

            process.Start();

            var outputTask =
                process.StandardOutput.ReadToEndAsync(
                    cancellationToken);

            var errorTask =
                process.StandardError.ReadToEndAsync(
                    cancellationToken);

            using var timeoutSource =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);

            timeoutSource.CancelAfter(
                timeout
                ?? TimeSpan.FromSeconds(45));

            try
            {
                await process.WaitForExitAsync(
                    timeoutSource.Token);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(
                        true);
                }
                catch
                {
                    // Best effort.
                }

                return
                    new ProcessResult(
                        -1,
                        "Process timed out.");
            }

            var output =
                await outputTask;

            var error =
                await errorTask;

            var combined =
                output;

            if (!string.IsNullOrWhiteSpace(
                    error))
            {
                combined +=
                    Environment.NewLine +
                    error;
            }

            return
                new ProcessResult(
                    process.ExitCode,
                    combined.Trim());
        }
        catch (Exception exception)
        {
            return
                new ProcessResult(
                    -1,
                    exception.Message);
        }
    }

    private static string TrimOutput(
        string output,
        int maximumLength)
    {
        if (output.Length <= maximumLength)
        {
            return output;
        }

        return
            output[..maximumLength]
            + Environment.NewLine
            + Environment.NewLine
            + "[Output truncated]";
    }

    private sealed record ProcessResult(
        int ExitCode,
        string Output);
}