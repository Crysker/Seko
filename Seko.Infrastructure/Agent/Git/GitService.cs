using System.Diagnostics;

namespace Seko.Infrastructure.Agent.Git;

public sealed class GitService
{
    private readonly string _workspaceRoot;

    public GitService(
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

    public async Task<GitRepositoryState> GetRepositoryStateAsync(
        CancellationToken cancellationToken = default)
    {
        var gitCheck =
            await RunGitAsync(
                new[]
                {
                    "rev-parse",
                    "--is-inside-work-tree"
                },
                cancellationToken);

        var isRepository =
            gitCheck.ExitCode == 0
            && gitCheck.Output.Trim().Equals(
                "true",
                StringComparison.OrdinalIgnoreCase);

        if (!isRepository)
        {
            return
                new GitRepositoryState(
                    false,
                    true);
        }

        var status =
            await RunGitAsync(
                new[]
                {
                    "status",
                    "--porcelain"
                },
                cancellationToken);

        var isClean =
            status.ExitCode == 0
            && string.IsNullOrWhiteSpace(
                status.Output);

        return
            new GitRepositoryState(
                true,
                isClean);
    }

    public Task<GitCommandResult> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        return
            RunGitAsync(
                new[]
                {
                    "status",
                    "--short"
                },
                cancellationToken);
    }

    public Task<GitCommandResult> GetDiffAsync(
        CancellationToken cancellationToken = default)
    {
        return
            RunGitAsync(
                new[]
                {
                    "diff"
                },
                cancellationToken);
    }

    public async Task<GitCommitResult> CommitAsync(
        IEnumerable<string> relativePaths,
        string userRequest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            relativePaths);

        var filesToStage =
            relativePaths
                .Where(
                    path =>
                        !string.IsNullOrWhiteSpace(
                            path))
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .OrderBy(
                    path => path,
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

        if (filesToStage.Count == 0)
        {
            return
                new GitCommitResult(
                    true,
                    false,
                    false,
                    string.Empty,
                    string.Empty,
                    string.Empty);
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
            await RunGitAsync(
                addArguments,
                cancellationToken);

        if (addResult.ExitCode != 0)
        {
            return
                new GitCommitResult(
                    false,
                    false,
                    false,
                    addResult.Output,
                    string.Empty,
                    string.Empty);
        }

        var stagedDiff =
            await RunGitAsync(
                new[]
                {
                    "diff",
                    "--cached",
                    "--name-only"
                },
                cancellationToken);

        if (string.IsNullOrWhiteSpace(
                stagedDiff.Output))
        {
            return
                new GitCommitResult(
                    true,
                    false,
                    false,
                    string.Empty,
                    string.Empty,
                    string.Empty);
        }

        var commitMessage =
            CreateCommitMessage(
                userRequest);

        var commitResult =
            await RunGitAsync(
                new[]
                {
                    "commit",
                    "-m",
                    commitMessage
                },
                cancellationToken);

        if (commitResult.ExitCode != 0)
        {
            return
                new GitCommitResult(
                    true,
                    true,
                    false,
                    commitResult.Output,
                    commitMessage,
                    string.Empty);
        }

        var hashResult =
            await RunGitAsync(
                new[]
                {
                    "rev-parse",
                    "--short",
                    "HEAD"
                },
                cancellationToken);

        return
            new GitCommitResult(
                true,
                true,
                true,
                string.Empty,
                commitMessage,
                hashResult.Output.Trim());
    }

    public static string CreateCommitMessage(
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

    private async Task<GitCommandResult> RunGitAsync(
        IEnumerable<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo =
            new ProcessStartInfo
            {
                FileName =
                    "git",

                WorkingDirectory =
                    _workspaceRoot,

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
                TimeSpan.FromSeconds(45));

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
                    new GitCommandResult(
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
                new GitCommandResult(
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
                new GitCommandResult(
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
}
