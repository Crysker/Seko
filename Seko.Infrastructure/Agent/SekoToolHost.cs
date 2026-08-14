using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
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

    private static readonly HashSet<string> SearchStopWords =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "a",
            "an",
            "and",
            "the",
            "to",
            "of",
            "for",
            "in",
            "on",
            "at",
            "with",
            "from",
            "my",
            "your",
            "you",
            "this",
            "that",
            "it",
            "its",
            "please",
            "change",
            "update",
            "modify",
            "edit",
            "make",
            "set",
            "replace",
            "fix",
            "implement",
            "add",
            "remove",
            "slightly",
            "more",
            "less",
            "smaller",
            "larger",
            "compact",
            "current",
            "new"
        };

    private static readonly Regex SearchTokenRegex =
        new(
            @"[A-Za-z0-9_.#-]+",
            RegexOptions.Compiled);

    private static readonly Regex VersionRegex =
        new(
            @"\bv?\d+\.\d+(?:\.\d+){0,2}(?:[-+][0-9A-Za-z.-]+)?\b",
            RegexOptions.Compiled
            | RegexOptions.IgnoreCase);

    private static readonly Regex DisplayVersionRegex =
        new(
            @"(?:Text\s*=\s*[""']|>\s*)v?\d+\.\d+(?:\.\d+){0,2}",
            RegexOptions.Compiled
            | RegexOptions.IgnoreCase);

    private readonly Workspace _workspace;
    private readonly string _workspaceRoot;

    private readonly HashSet<string> _changedFiles =
        new(StringComparer.OrdinalIgnoreCase);

    private bool _isGitRepository;
    private bool _baselineClean = true;

    private bool _buildWasRun;
    private bool _lastBuildSucceeded;

    /*
        These generations prevent an old successful build from validating
        source code that was modified afterward.
    */
    private int _buildRelevantModificationGeneration;
    private int _lastSuccessfulBuildGeneration = -1;

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

        _buildRelevantModificationGeneration =
            0;

        _lastSuccessfulBuildGeneration =
            -1;

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
                "search_workspace",
                """
                Search the entire active workspace for a concept, feature, UI element,
                symbol, version number or text.

                This searches BOTH filenames and textual file contents, ranks the most
                relevant results and returns matching line numbers with small snippets.

                Prefer this when the user describes WHAT they want rather than giving an
                exact filename.

                Examples:
                - version
                - activity panel
                - sidebar
                - login button
                - player health
                - model selector
                """,
                new JsonObject
                {
                    ["type"] =
                        "object",

                    ["properties"] =
                        new JsonObject
                        {
                            ["query"] =
                                StringProperty(
                                    "Concept, feature, symbol or text to locate in the workspace."),

                            ["max_results"] =
                                new JsonObject
                                {
                                    ["type"] =
                                        "integer",

                                    ["description"] =
                                        "Maximum ranked results to return. Usually 6 to 12.",

                                    ["minimum"] =
                                        1,

                                    ["maximum"] =
                                        20
                                }
                        },

                    ["required"] =
                        new JsonArray
                        {
                            "query"
                        }
                }),

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
                "Find text inside one known file and return matching lines plus nearby context. Prefer this over read_file for focused inspection.",
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
                "List files and directories inside a specific workspace directory. Use this for directory overviews, not for locating one conceptual target.",
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
                "read_task_log",
                """
                Read one of Seko's own finished diagnostic task logs from the real
                Windows LocalApplicationData\Seko\Logs\Tasks directory.

                Use selection 'latest' for the newest finished task.
                Use selection 'latest_unsuccessful' for the newest failed,
                incomplete or stopped task.

                This tool is read-only and cannot access arbitrary paths.
                """,
                new JsonObject
                {
                    ["type"] =
                        "object",

                    ["properties"] =
                        new JsonObject
                        {
                            ["selection"] =
                                new JsonObject
                                {
                                    ["type"] =
                                        "string",

                                    ["description"] =
                                        "Which finished task log to read.",

                                    ["enum"] =
                                        new JsonArray
                                        {
                                            "latest",
                                            "latest_unsuccessful"
                                        }
                                }
                        },

                    ["required"] =
                        new JsonArray()
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
                """
                Replace exactly one matching section in an existing source file.

                old_text must be copied from actual workspace evidence and must occur
                exactly once. If OLD_TEXT_NOT_FOUND is returned, inspect the real source
                again instead of repeating the same failed replacement.
                """,
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
                """
                Build the active .NET workspace.

                This tool automatically prefers a solution file at the workspace root.
                The model does NOT need to guess or specify a .csproj when an appropriate
                solution exists.
                """,
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
                "search_workspace" =>
                    await SearchWorkspaceAsync(
                        arguments,
                        cancellationToken),

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

                "read_task_log" =>
                    await ReadTaskLogAsync(
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
        catch (OperationCanceledException)
        {
            throw;
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
                || !_lastBuildSucceeded
                || _lastSuccessfulBuildGeneration
                    < _buildRelevantModificationGeneration))
        {
            return
                "Git: changes were not committed because a successful build after the final build-relevant modification has not been verified.";
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
            $"{hashResult.Output.Trim()} - " +
            commitMessage;
    }

    private async Task<string> SearchWorkspaceAsync(
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var rawQuery =
            GetString(
                arguments,
                "query")
            .Trim();

        if (string.IsNullOrWhiteSpace(
                rawQuery))
        {
            return
                "ERROR: Workspace search query cannot be empty.";
        }

        var maximumResults =
            Math.Clamp(
                GetOptionalInteger(
                    arguments,
                    "max_results",
                    10),
                1,
                20);

        var queryTerms =
            ExtractSearchTerms(
                rawQuery);

        var normalizedQuery =
            NormalizeSearchText(
                rawQuery);

        var compactQuery =
            CompactText(
                rawQuery);

        var versionIntent =
            rawQuery.Contains(
                "version",
                StringComparison.OrdinalIgnoreCase)
            || VersionRegex.IsMatch(
                rawQuery);

        const int maximumFilesToScan =
            2500;

        const long maximumSearchFileSize =
            400_000;

        var results =
            new List<WorkspaceSearchResult>();

        var scannedFiles =
            0;

        foreach (var file
                 in EnumerateWorkspaceFiles(
                     maximumFilesToScan))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsSensitiveFile(
                    file))
            {
                continue;
            }

            if (!IsSearchableFile(
                    file))
            {
                continue;
            }

            var fileInfo =
                new FileInfo(
                    file);

            if (!fileInfo.Exists
                || fileInfo.Length
                    > maximumSearchFileSize)
            {
                continue;
            }

            scannedFiles++;

            var relativePath =
                ToRelativePath(
                    file);

            var fileName =
                Path.GetFileName(
                    file);

            var fileScore =
                ScoreFileName(
                    fileName,
                    relativePath,
                    normalizedQuery,
                    compactQuery,
                    queryTerms);

            string[] lines;

            try
            {
                lines =
                    await File.ReadAllLinesAsync(
                        file,
                        cancellationToken);
            }
            catch
            {
                continue;
            }

            var lineMatches =
                new List<WorkspaceLineMatch>();

            for (var lineIndex = 0;
                 lineIndex < lines.Length;
                 lineIndex++)
            {
                var line =
                    lines[lineIndex];

                var score =
                    ScoreContentLine(
                        line,
                        normalizedQuery,
                        compactQuery,
                        queryTerms,
                        versionIntent);

                if (score <= 0)
                {
                    continue;
                }

                lineMatches.Add(
                    new WorkspaceLineMatch(
                        lineIndex,
                        score));
            }

            if (fileScore <= 0
                && lineMatches.Count == 0)
            {
                continue;
            }

            var strongestMatches =
                lineMatches
                    .OrderByDescending(
                        match => match.Score)
                    .ThenBy(
                        match => match.LineIndex)
                    .Take(3)
                    .ToList();

            var contentScore =
                strongestMatches.Sum(
                    match => match.Score);

            var scoreTotal =
                fileScore
                + contentScore;

            results.Add(
                new WorkspaceSearchResult(
                    relativePath,
                    scoreTotal,
                    fileScore,
                    lines,
                    strongestMatches));
        }

        var ranked =
            results
                .OrderByDescending(
                    result => result.Score)
                .ThenBy(
                    result => result.RelativePath,
                    StringComparer.OrdinalIgnoreCase)
                .Take(
                    maximumResults)
                .ToList();

        if (ranked.Count == 0)
        {
            return
                $"No relevant accessible workspace matches were found for '{rawQuery}'. " +
                $"Scanned {scannedFiles} searchable files.";
        }

        var builder =
            new StringBuilder();

        builder.AppendLine(
            $"WORKSPACE SEARCH: {rawQuery}");

        builder.AppendLine(
            $"SCANNED FILES: {scannedFiles}");

        builder.AppendLine(
            $"RESULTS: {ranked.Count}");

        builder.AppendLine();

        for (var resultIndex = 0;
             resultIndex < ranked.Count;
             resultIndex++)
        {
            var result =
                ranked[resultIndex];

            builder.AppendLine(
                $"#{resultIndex + 1} {result.RelativePath}");

            builder.AppendLine(
                $"RELEVANCE SCORE: {result.Score}");

            if (result.LineMatches.Count == 0)
            {
                builder.AppendLine(
                    "MATCH: filename/path");

                builder.AppendLine();

                continue;
            }

            foreach (var lineMatch
                     in result.LineMatches)
            {
                var start =
                    Math.Max(
                        0,
                        lineMatch.LineIndex - 2);

                var end =
                    Math.Min(
                        result.Lines.Length - 1,
                        lineMatch.LineIndex + 2);

                builder.AppendLine(
                    $"--- Match at line {lineMatch.LineIndex + 1} ---");

                for (var index = start;
                     index <= end;
                     index++)
                {
                    var marker =
                        index == lineMatch.LineIndex
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
                        TruncateLine(
                            result.Lines[index],
                            300));
                }

                builder.AppendLine();
            }
        }

        return
            builder
                .ToString()
                .TrimEnd();
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

            foreach (var directory
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

            foreach (var file
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

                if (results.Count
                    >= maximumResults)
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
            Math.Clamp(
                GetOptionalInteger(
                    arguments,
                    "context_lines",
                    4),
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

        foreach (var matchIndex
                 in matchingLines)
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

        return
            builder
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

            foreach (var childDirectory
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

                if (results.Count
                    >= maximumEntries)
                {
                    break;
                }
            }

            if (results.Count
                >= maximumEntries)
            {
                break;
            }

            foreach (var file
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

                if (results.Count
                    >= maximumEntries)
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

    private static async Task<string> ReadTaskLogAsync(
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var selection =
            "latest";

        if (arguments.TryGetProperty(
                "selection",
                out var selectionElement)
            && selectionElement.ValueKind
                == JsonValueKind.String)
        {
            selection =
                selectionElement.GetString()
                    ?.Trim()
                    .ToLowerInvariant()
                ?? "latest";
        }

        if (!string.Equals(
                selection,
                "latest",
                StringComparison.Ordinal)
            && !string.Equals(
                selection,
                "latest_unsuccessful",
                StringComparison.Ordinal))
        {
            return
                "ERROR: selection must be 'latest' or 'latest_unsuccessful'.";
        }

        var localAppData =
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);

        var logDirectory =
            Path.Combine(
                localAppData,
                "Seko",
                "Logs",
                "Tasks");

        if (!Directory.Exists(
                logDirectory))
        {
            return
                "No Seko task log directory exists yet.";
        }

        List<FileInfo> logFiles;

        try
        {
            logFiles =
                new DirectoryInfo(
                    logDirectory)
                    .EnumerateFiles(
                        "*.md",
                        SearchOption.TopDirectoryOnly)
                    .OrderByDescending(
                        file => file.LastWriteTimeUtc)
                    .ToList();
        }
        catch (Exception exception)
        {
            return
                "ERROR: Could not enumerate Seko task logs: " +
                exception.Message;
        }

        if (logFiles.Count == 0)
        {
            return
                "No Seko task logs were found.";
        }

        foreach (var logFile
                 in logFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (logFile.Length
                > 500_000)
            {
                continue;
            }

            string content;

            try
            {
                content =
                    await File.ReadAllTextAsync(
                        logFile.FullName,
                        cancellationToken);
            }
            catch
            {
                continue;
            }

            /*
                Starting a new Seko request creates its own Running log before
                tools execute. Skip that current log so "latest" means the
                newest task that actually finished before this request.
            */
            if (content.Contains(
                    "Status: **Running**",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (selection
                    == "latest_unsuccessful"
                && !IsUnsuccessfulTaskLog(
                    content))
            {
                continue;
            }

            return
                $"TASK LOG FILE: {logFile.Name}\n" +
                $"SELECTION: {selection}\n\n" +
                TrimOutput(
                    content,
                    80_000);
        }

        return selection
            == "latest_unsuccessful"
                ? "No finished failed, incomplete or stopped Seko task log was found."
                : "No finished Seko task log was found.";
    }

    private static bool IsUnsuccessfulTaskLog(
        string content)
    {
        return
            content.Contains(
                "Status: **Failed**",
                StringComparison.OrdinalIgnoreCase)

            || content.Contains(
                "Status: **Incomplete**",
                StringComparison.OrdinalIgnoreCase)

            || content.Contains(
                "Status: **Stopped**",
                StringComparison.OrdinalIgnoreCase);
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

        if (File.Exists(
                fullPath))
        {
            var currentContent =
                await File.ReadAllTextAsync(
                    fullPath,
                    cancellationToken);

            if (string.Equals(
                    currentContent,
                    content,
                    StringComparison.Ordinal))
            {
                return
                    $"No change needed in {NormalizeRelativePath(relativePath)}.";
            }
        }

        await WritePreservingUtf8BomAsync(
            fullPath,
            content,
            cancellationToken);

        var normalizedPath =
            NormalizeRelativePath(
                Path.GetRelativePath(
                    _workspaceRoot,
                    fullPath));

        RegisterChangedFile(
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
                """
                ERROR: OLD_TEXT_NOT_FOUND.

                The supplied old_text does not exactly match the current file.

                Re-inspect the relevant source using find_text or search_workspace,
                copy the real current text and retry with a corrected unique match.

                Do not repeat the same failed replacement unchanged.
                """;
        }

        if (occurrences > 1)
        {
            return
                $"ERROR: old_text appears {occurrences} times. Inspect the surrounding source and use a more specific unique section.";
        }

        if (string.Equals(
                oldText,
                newText,
                StringComparison.Ordinal))
        {
            return
                $"No change needed in {NormalizeRelativePath(relativePath)}.";
        }

        var updatedContent =
            content.Replace(
                oldText,
                newText,
                StringComparison.Ordinal);

        await WritePreservingUtf8BomAsync(
            fullPath,
            updatedContent,
            cancellationToken);

        var normalizedPath =
            NormalizeRelativePath(
                Path.GetRelativePath(
                    _workspaceRoot,
                    fullPath));

        RegisterChangedFile(
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

        if (_lastBuildSucceeded)
        {
            _lastSuccessfulBuildGeneration =
                _buildRelevantModificationGeneration;
        }

        return
            $"BUILD TARGET: {ToRelativePath(target)}\n" +
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

    private void RegisterChangedFile(
        string normalizedPath)
    {
        _changedFiles.Add(
            normalizedPath);

        if (RequiresBuild(
                normalizedPath))
        {
            _buildRelevantModificationGeneration++;
        }
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
                .OrderBy(
                    path => path,
                    StringComparer.OrdinalIgnoreCase)
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
            .Where(
                path =>
                    !PathContainsIgnoredDirectory(
                        Path.GetRelativePath(
                            _workspaceRoot,
                            path)))
            .OrderBy(
                path => path,
                StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private IEnumerable<string> EnumerateWorkspaceFiles(
        int maximumFiles)
    {
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

                if (IgnoredDirectories.Contains(
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

    private static bool IsSearchableFile(
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

    private static int ScoreFileName(
        string fileName,
        string relativePath,
        string normalizedQuery,
        string compactQuery,
        IReadOnlyList<string> terms)
    {
        var score =
            0;

        var normalizedFileName =
            NormalizeSearchText(
                fileName);

        var normalizedPath =
            NormalizeSearchText(
                relativePath);

        var compactFileName =
            CompactText(
                fileName);

        if (normalizedQuery.Length >= 3
            && normalizedFileName.Contains(
                normalizedQuery,
                StringComparison.OrdinalIgnoreCase))
        {
            score +=
                120;
        }

        if (compactQuery.Length >= 4
            && compactFileName.Contains(
                compactQuery,
                StringComparison.OrdinalIgnoreCase))
        {
            score +=
                110;
        }

        foreach (var term
                 in terms)
        {
            if (normalizedFileName.Contains(
                    term,
                    StringComparison.OrdinalIgnoreCase))
            {
                score +=
                    35;
            }
            else if (normalizedPath.Contains(
                         term,
                         StringComparison.OrdinalIgnoreCase))
            {
                score +=
                    12;
            }
        }

        return score;
    }

    private static int ScoreContentLine(
        string line,
        string normalizedQuery,
        string compactQuery,
        IReadOnlyList<string> terms,
        bool versionIntent)
    {
        if (string.IsNullOrWhiteSpace(
                line))
        {
            return 0;
        }

        var score =
            0;

        var normalizedLine =
            NormalizeSearchText(
                line);

        var compactLine =
            CompactText(
                line);

        if (normalizedQuery.Length >= 3
            && normalizedLine.Contains(
                normalizedQuery,
                StringComparison.OrdinalIgnoreCase))
        {
            score +=
                140;
        }

        if (compactQuery.Length >= 4
            && compactLine.Contains(
                compactQuery,
                StringComparison.OrdinalIgnoreCase))
        {
            score +=
                120;
        }

        var matchedTerms =
            0;

        foreach (var term
                 in terms)
        {
            if (normalizedLine.Contains(
                    term,
                    StringComparison.OrdinalIgnoreCase)
                || compactLine.Contains(
                    CompactText(term),
                    StringComparison.OrdinalIgnoreCase))
            {
                matchedTerms++;

                score +=
                    24;
            }
        }

        if (terms.Count > 1
            && matchedTerms >= 2)
        {
            score +=
                35;
        }

        if (versionIntent
            && VersionRegex.IsMatch(
                line))
        {
            score +=
                90;

            if (DisplayVersionRegex.IsMatch(
                    line))
            {
                score +=
                    120;
            }

            if (line.Contains(
                    "Version",
                    StringComparison.OrdinalIgnoreCase))
            {
                score +=
                    30;
            }
        }

        return score;
    }

    private static List<string> ExtractSearchTerms(
        string query)
    {
        return
            SearchTokenRegex
                .Matches(
                    query)
                .Cast<Match>()
                .Select(
                    match =>
                        match.Value.Trim())
                .Where(
                    value =>
                        value.Length >= 2)
                .Where(
                    value =>
                        !SearchStopWords.Contains(
                            value))
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToList();
    }

    private static string NormalizeSearchText(
        string value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return string.Empty;
        }

        var builder =
            new StringBuilder(
                value.Length);

        var previousWasSpace =
            false;

        foreach (var character
                 in value)
        {
            if (char.IsWhiteSpace(
                    character))
            {
                if (!previousWasSpace)
                {
                    builder.Append(
                        ' ');

                    previousWasSpace =
                        true;
                }

                continue;
            }

            builder.Append(
                char.ToLowerInvariant(
                    character));

            previousWasSpace =
                false;
        }

        return
            builder
                .ToString()
                .Trim();
    }

    private static string CompactText(
        string value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return string.Empty;
        }

        var builder =
            new StringBuilder(
                value.Length);

        foreach (var character
                 in value)
        {
            if (!char.IsLetterOrDigit(
                    character))
            {
                continue;
            }

            builder.Append(
                char.ToLowerInvariant(
                    character));
        }

        return
            builder.ToString();
    }

    private static string TruncateLine(
        string line,
        int maximumLength)
    {
        if (line.Length
            <= maximumLength)
        {
            return line;
        }

        return
            line[..maximumLength]
            + "...";
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
            || element.ValueKind
                != JsonValueKind.String)
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

        if (element.ValueKind
            == JsonValueKind.True)
        {
            return true;
        }

        if (element.ValueKind
            == JsonValueKind.False)
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

        if (element.ValueKind
            == JsonValueKind.Number
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
                firstLine[..60]
                    .TrimEnd()
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

    private static async Task WritePreservingUtf8BomAsync(
        string fullPath,
        string content,
        CancellationToken cancellationToken)
    {
        var useBom =
            false;

        if (File.Exists(
                fullPath))
        {
            try
            {
                await using var stream =
                    new FileStream(
                        fullPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite,
                        3,
                        FileOptions.Asynchronous);

                var prefix =
                    new byte[3];

                var read =
                    await stream.ReadAsync(
                        prefix.AsMemory(
                            0,
                            prefix.Length),
                        cancellationToken);

                useBom =
                    read >= 3
                    && prefix[0] == 0xEF
                    && prefix[1] == 0xBB
                    && prefix[2] == 0xBF;
            }
            catch
            {
                useBom =
                    false;
            }
        }

        await File.WriteAllTextAsync(
            fullPath,
            content,
            new UTF8Encoding(
                useBom),
            cancellationToken);
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

        foreach (var argument
                 in arguments)
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
                process.StandardOutput.ReadToEndAsync();

            var errorTask =
                process.StandardError.ReadToEndAsync();

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
                when (cancellationToken.IsCancellationRequested)
            {
                TryKillProcess(
                    process);

                throw;
            }
            catch (OperationCanceledException)
            {
                TryKillProcess(
                    process);

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
                if (!string.IsNullOrWhiteSpace(
                        combined))
                {
                    combined +=
                        Environment.NewLine;
                }

                combined +=
                    error;
            }

            return
                new ProcessResult(
                    process.ExitCode,
                    combined.Trim());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return
                new ProcessResult(
                    -1,
                    exception.Message);
        }
    }

    private static void TryKillProcess(
        Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(
                    true);
            }
        }
        catch
        {
            // Best effort.
        }
    }

    private static string TrimOutput(
        string output,
        int maximumLength)
    {
        if (output.Length
            <= maximumLength)
        {
            return output;
        }

        return
            output[..maximumLength]
            + Environment.NewLine
            + Environment.NewLine
            + "[Output truncated]";
    }

    private sealed record WorkspaceLineMatch(
        int LineIndex,
        int Score);

    private sealed record WorkspaceSearchResult(
        string RelativePath,
        int Score,
        int FileScore,
        string[] Lines,
        IReadOnlyList<WorkspaceLineMatch> LineMatches);

    private sealed record ProcessResult(
        int ExitCode,
        string Output);
}