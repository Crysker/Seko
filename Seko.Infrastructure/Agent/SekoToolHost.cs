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
    private readonly string _backupRoot;

    private readonly HashSet<string> _changedFiles =
        new(StringComparer.OrdinalIgnoreCase);

    private bool _isGitRepository;
    private bool _baselineClean = true;

    private bool _buildWasRun;
    private bool _lastBuildSucceeded;

    public SekoToolHost(
        Workspace workspace)
    {
        _workspace = workspace;

        _workspaceRoot =
            Path.GetFullPath(
                workspace.RootPath);

        var localAppData =
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);

        _backupRoot =
            Path.Combine(
                localAppData,
                "Seko",
                "Backups",
                workspace.Id.ToString("N"));
    }

    public async Task BeginTaskAsync(
        CancellationToken cancellationToken = default)
    {
        _changedFiles.Clear();

        _buildWasRun = false;
        _lastBuildSucceeded = false;

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
            _baselineClean = true;
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
                "list_files",
                "List files and folders inside the active workspace.",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] =
                        new JsonObject
                        {
                            ["path"] =
                                StringProperty(
                                    "Path relative to the workspace root. Use an empty string for the root."),

                            ["recursive"] =
                                new JsonObject
                                {
                                    ["type"] = "boolean",
                                    ["description"] =
                                        "Whether to recursively list child folders."
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
                "Read a text or source-code file inside the active workspace.",
                new JsonObject
                {
                    ["type"] = "object",
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
                "Create a new text/source file or completely replace an existing one. Existing files are backed up first.",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] =
                        new JsonObject
                        {
                            ["path"] =
                                StringProperty(
                                    "File path relative to the workspace root."),

                            ["content"] =
                                StringProperty(
                                    "Complete contents of the new file.")
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
                "Replace exactly one matching section in an existing source file. Prefer this for small edits.",
                new JsonObject
                {
                    ["type"] = "object",
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
                "Run dotnet build for the active .NET workspace and return compiler output.",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] =
                        new JsonObject(),
                    ["required"] =
                        new JsonArray()
                }),

            CreateFunctionTool(
                "git_status",
                "Inspect Git status for the active workspace.",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] =
                        new JsonObject(),
                    ["required"] =
                        new JsonArray()
                }),

            CreateFunctionTool(
                "git_diff",
                "Show the current Git diff.",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] =
                        new JsonObject(),
                    ["required"] =
                        new JsonArray()
                })
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
                    string.IsNullOrWhiteSpace(argumentsJson)
                        ? "{}"
                        : argumentsJson);

            var arguments =
                document.RootElement;

            return toolName switch
            {
                "list_files" =>
                    await ListFilesAsync(
                        arguments,
                        cancellationToken),

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
                "Git: automatic commit skipped because the repository already had uncommitted changes before I started.";
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

        var existingFiles =
            _changedFiles
                .Where(
                    relativePath =>
                        File.Exists(
                            Path.Combine(
                                _workspaceRoot,
                                relativePath)))
                .ToList();

        if (existingFiles.Count == 0)
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
            existingFiles);

        var addResult =
            await RunProcessAsync(
                "git",
                addArguments,
                _workspaceRoot,
                cancellationToken);

        if (addResult.ExitCode != 0)
        {
            return
                "Git: failed to stage Seko's changes.\n" +
                addResult.Output;
        }

        var stagedFiles =
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
                stagedFiles.Output))
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
                "Git: changes were staged but the commit failed.\n" +
                commitResult.Output;
        }

        var hash =
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
            $"Git: committed automatically as " +
            $"{hash.Output.Trim()} — {commitMessage}";
    }

    private Task<string> ListFilesAsync(
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var relativePath =
            GetString(
                arguments,
                "path");

        var recursive =
            arguments.TryGetProperty(
                "recursive",
                out var recursiveElement)
            && recursiveElement.ValueKind
                == JsonValueKind.True;

        var directory =
            ResolveSafePath(
                relativePath);

        if (!Directory.Exists(
                directory))
        {
            return Task.FromResult(
                $"ERROR: Directory not found: {relativePath}");
        }

        const int maximumEntries = 300;

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
                in Directory.EnumerateDirectories(current)
                    .OrderBy(
                        path => path,
                        StringComparer.OrdinalIgnoreCase))
            {
                var name =
                    Path.GetFileName(
                        childDirectory);

                if (IgnoredDirectories.Contains(
                        name))
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
                in Directory.EnumerateFiles(current)
                    .OrderBy(
                        path => path,
                        StringComparer.OrdinalIgnoreCase))
            {
                if (IsSensitiveFile(file))
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
            return Task.FromResult(
                "Directory is empty.");
        }

        return Task.FromResult(
            string.Join(
                Environment.NewLine,
                results));
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

        if (!File.Exists(fullPath))
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
            $"FILE: {relativePath}\n\n{content}";
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

        if (File.Exists(fullPath))
        {
            BackupFile(
                fullPath,
                relativePath);
        }

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

        var normalized =
            NormalizeRelativePath(
                relativePath);

        _changedFiles.Add(
            normalized);

        return
            $"Wrote {normalized}.";
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

        var fullPath =
            ResolveSafePath(
                relativePath);

        EnsureAllowedFile(
            fullPath);

        if (!File.Exists(fullPath))
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
                "ERROR: old_text was not found. Read the current file before editing.";
        }

        if (occurrences > 1)
        {
            return
                $"ERROR: old_text occurs {occurrences} times. Use a more specific section.";
        }

        BackupFile(
            fullPath,
            relativePath);

        var newContent =
            content.Replace(
                oldText,
                newText,
                StringComparison.Ordinal);

        await File.WriteAllTextAsync(
            fullPath,
            newContent,
            new UTF8Encoding(false),
            cancellationToken);

        var normalized =
            NormalizeRelativePath(
                relativePath);

        _changedFiles.Add(
            normalized);

        return
            $"Updated {normalized}.";
    }

    private async Task<string> BuildProjectAsync(
        CancellationToken cancellationToken)
    {
        _buildWasRun = true;
        _lastBuildSucceeded = false;

        var target =
            FindBuildTarget();

        if (target is null)
        {
            return
                "ERROR: No .sln or .csproj file was found.";
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

        var status =
            string.IsNullOrWhiteSpace(
                result.Output)
                ? "Working tree clean."
                : result.Output;

        return
            $"Repository clean when task began: {_baselineClean}\n\n" +
            status;
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

        return
            string.IsNullOrWhiteSpace(
                result.Output)
                ? "No unstaged Git diff."
                : TrimOutput(
                    result.Output,
                    30_000);
    }

    private void EnsureModificationAllowed()
    {
        if (_isGitRepository
            && !_baselineClean)
        {
            throw new InvalidOperationException(
                "This Git repository already had uncommitted changes before Seko started this task. " +
                "For safety, Seko will not modify files until those changes are committed or reverted.");
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

        var prefix =
            root +
            Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException(
                "Path escapes the active workspace.");
        }

        var relative =
            Path.GetRelativePath(
                root,
                fullPath);

        var parts =
            relative.Split(
                new[]
                {
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar
                },
                StringSplitOptions.RemoveEmptyEntries);

        if (parts.Any(
                part =>
                    IgnoredDirectories.Contains(part)))
        {
            throw new UnauthorizedAccessException(
                "Direct access to generated or Git-internal directories is blocked.");
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

        var name =
            Path.GetFileName(
                fullPath);

        if (name.Equals(
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
                $"File type '{extension}' is not enabled yet.");
        }
    }

    private static bool IsSensitiveFile(
        string fullPath)
    {
        var name =
            Path.GetFileName(
                fullPath);

        var extension =
            Path.GetExtension(
                fullPath);

        return
            name.Equals(
                ".env",
                StringComparison.OrdinalIgnoreCase)

            || name.StartsWith(
                ".env.",
                StringComparison.OrdinalIgnoreCase)

            || name.Equals(
                "secrets.json",
                StringComparison.OrdinalIgnoreCase)

            || name.Equals(
                "credentials.json",
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

    private void BackupFile(
        string fullPath,
        string relativePath)
    {
        if (!File.Exists(
                fullPath))
        {
            return;
        }

        var timestamp =
            DateTime.Now.ToString(
                "yyyyMMdd-HHmmssfff");

        var backupPath =
            Path.Combine(
                _backupRoot,
                timestamp,
                relativePath);

        var backupDirectory =
            Path.GetDirectoryName(
                backupPath);

        if (!string.IsNullOrWhiteSpace(
                backupDirectory))
        {
            Directory.CreateDirectory(
                backupDirectory);
        }

        File.Copy(
            fullPath,
            backupPath,
            true);
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
                        path));
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

        return parts.Any(
            part =>
                IgnoredDirectories.Contains(
                    part));
    }

    private string ToRelativePath(
        string fullPath)
    {
        return NormalizeRelativePath(
            Path.GetRelativePath(
                _workspaceRoot,
                fullPath));
    }

    private static string NormalizeRelativePath(
        string path)
    {
        return path.Replace(
            '\\',
            '/');
    }

    private static string GetString(
        JsonElement arguments,
        string property)
    {
        if (!arguments.TryGetProperty(
                property,
                out var element)
            || element.ValueKind
                != JsonValueKind.String)
        {
            throw new ArgumentException(
                $"Missing string argument '{property}'.");
        }

        return element.GetString()
               ?? string.Empty;
    }

    private static int CountOccurrences(
        string text,
        string value)
    {
        if (string.IsNullOrEmpty(
                value))
        {
            return 0;
        }

        var count = 0;
        var index = 0;

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

        return extension.Equals(
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
        string request)
    {
        var firstLine =
            request
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

        if (firstLine.Length > 58)
        {
            firstLine =
                firstLine[..58]
                .TrimEnd()
                + "...";
        }

        return
            $"Seko: {firstLine}";
    }

    private static JsonObject StringProperty(
        string description)
    {
        return new JsonObject
        {
            ["type"] = "string",
            ["description"] = description
        };
    }

    private static JsonObject CreateFunctionTool(
        string name,
        string description,
        JsonObject parameters)
    {
        parameters["additionalProperties"] =
            false;

        return new JsonObject
        {
            ["type"] = "function",

            ["function"] =
                new JsonObject
                {
                    ["name"] = name,
                    ["description"] = description,
                    ["parameters"] = parameters
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
                FileName = executable,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
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
                    StartInfo = startInfo
                };

            process.Start();

            var outputTask =
                process.StandardOutput.ReadToEndAsync();

            var errorTask =
                process.StandardError.ReadToEndAsync();

            using var timeoutSource =
                CancellationTokenSource
                    .CreateLinkedTokenSource(
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

                return new ProcessResult(
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
                    "\n" +
                    error;
            }

            return new ProcessResult(
                process.ExitCode,
                combined.Trim());
        }
        catch (Exception exception)
        {
            return new ProcessResult(
                -1,
                exception.Message);
        }
    }

    private static string TrimOutput(
        string value,
        int maximumLength)
    {
        if (value.Length <= maximumLength)
        {
            return value;
        }

        return
            value[..maximumLength]
            + "\n\n[Output truncated]";
    }

    private sealed record ProcessResult(
        int ExitCode,
        string Output);
}