using System.Diagnostics;
using Seko.Core.Agent;
using Seko.Core.Chat;
using Seko.Core.Workspaces;
using Seko.Infrastructure.Diagnostics;

namespace Seko.Infrastructure.Agent;

public sealed class SekoTransactionalAgent :
    IAgent,
    IAgentActivitySource
{
    private readonly Workspace _workspace;
    private readonly IAgent _innerAgent;
    private readonly SekoTaskLogger _taskLogger;

    private bool _completedObserved;

    public event Action<AgentActivity>? ActivityChanged;

    public SekoTransactionalAgent(
        Workspace workspace,
        IAgent innerAgent)
    {
        _workspace =
            workspace;

        _innerAgent =
            innerAgent;

        _taskLogger =
            new SekoTaskLogger();

        if (_innerAgent
            is IAgentActivitySource activitySource)
        {
            activitySource.ActivityChanged +=
                InnerAgent_ActivityChanged;
        }
    }

    public async Task<ChatMessage> SendAsync(
        IReadOnlyList<ChatMessage> conversation,
        CancellationToken cancellationToken = default)
    {
        _completedObserved =
            false;

        var userRequest =
            conversation
                .LastOrDefault(
                    message =>
                        message.Role == MessageRole.User)
                ?.Content
            ?? "Seko task";

        var modelName =
            Environment.GetEnvironmentVariable(
                "SEKO_OLLAMA_MODEL")
            ?? "qwen3:8b";

        var logSession =
            _taskLogger.TryStart(
                _workspace,
                modelName,
                userRequest);

        var transaction =
            await GitTaskTransaction.BeginAsync(
                _workspace.RootPath,
                cancellationToken);

        try
        {
            var response =
                await _innerAgent.SendAsync(
                    conversation,
                    cancellationToken);

            if (_completedObserved)
            {
                _taskLogger.TryFinish(
                    logSession,
                    "Completed",
                    response.Content);

                return response;
            }

            var rollbackResult =
                await RollbackAsync(
                    transaction);

            var incompleteResponse =
                AppendRollbackResult(
                    response,
                    rollbackResult);

            _taskLogger.TryFinish(
                logSession,
                "Incomplete",
                incompleteResponse.Content);

            return incompleteResponse;
        }
        catch (OperationCanceledException)
        {
            var rollbackResult =
                await RollbackAsync(
                    transaction);

            var message =
                rollbackResult.Attempted
                    ? rollbackResult.Succeeded
                        ? "Stopped by user. Changes from the incomplete task were rolled back automatically."
                        : "Stopped by user. Automatic rollback was attempted but could not be fully verified."
                    : "Stopped by user.";

            _taskLogger.TryFinish(
                logSession,
                "Stopped",
                message);

            throw;
        }
        catch (Exception exception)
        {
            var rollbackResult =
                await RollbackAsync(
                    transaction);

            var message =
                $"Task failed: {exception.Message}";

            if (rollbackResult.Attempted)
            {
                message +=
                    rollbackResult.Succeeded
                        ? "\n\nChanges from the failed task were rolled back automatically."
                        : "\n\nAutomatic rollback was attempted but could not be fully verified.";
            }

            _taskLogger.TryFinish(
                logSession,
                "Failed",
                message);

            throw;
        }
    }

    private void InnerAgent_ActivityChanged(
        AgentActivity activity)
    {
        if (activity.Kind
            == AgentActivityKind.Completed)
        {
            _completedObserved =
                true;
        }

        ActivityChanged?.Invoke(
            activity);
    }

    private async Task<RollbackResult> RollbackAsync(
        GitTaskTransaction transaction)
    {
        if (!transaction.IsEnabled)
        {
            return RollbackResult.None;
        }

        var hasChanges =
            await transaction.HasChangesAsync(
                CancellationToken.None);

        if (!hasChanges)
        {
            return RollbackResult.None;
        }

        ActivityChanged?.Invoke(
            new AgentActivity(
                AgentActivityKind.Tool,
                "Rolling back incomplete task changes..."));

        var result =
            await transaction.RollbackAsync(
                CancellationToken.None);

        if (result.Succeeded)
        {
            ActivityChanged?.Invoke(
                new AgentActivity(
                    AgentActivityKind.Tool,
                    "Rollback complete. Workspace restored."));
        }
        else
        {
            ActivityChanged?.Invoke(
                new AgentActivity(
                    AgentActivityKind.Error,
                    "Rollback needs attention."));
        }

        return result;
    }

    private static ChatMessage AppendRollbackResult(
        ChatMessage response,
        RollbackResult rollbackResult)
    {
        if (!rollbackResult.Attempted)
        {
            return response;
        }

        var content =
            response.Content;

        if (!string.IsNullOrWhiteSpace(
                content))
        {
            content +=
                "\n\n";
        }

        content +=
            rollbackResult.Succeeded
                ? "Safety: changes from this incomplete task were rolled back automatically. The workspace was restored to its pre-task state."
                : "Safety: automatic rollback was attempted but could not be fully verified.\n\n" +
                  rollbackResult.Message;

        return
            new ChatMessage
            {
                Id =
                    response.Id,

                Role =
                    response.Role,

                Content =
                    content,

                CreatedAt =
                    response.CreatedAt
            };
    }

    private sealed class GitTaskTransaction
    {
        private readonly string _repositoryRoot;
        private readonly string? _baselineHead;

        private GitTaskTransaction(
            string repositoryRoot,
            string? baselineHead,
            bool isEnabled)
        {
            _repositoryRoot =
                repositoryRoot;

            _baselineHead =
                baselineHead;

            IsEnabled =
                isEnabled;
        }

        public bool IsEnabled
        {
            get;
        }

        public static async Task<GitTaskTransaction> BeginAsync(
            string workspaceRoot,
            CancellationToken cancellationToken)
        {
            var root =
                Path.GetFullPath(
                    workspaceRoot);

            var repositoryCheck =
                await RunGitAsync(
                    root,
                    cancellationToken,
                    "rev-parse",
                    "--is-inside-work-tree");

            if (repositoryCheck.ExitCode != 0
                || !repositoryCheck.Output.Trim().Equals(
                    "true",
                    StringComparison.OrdinalIgnoreCase))
            {
                return
                    new GitTaskTransaction(
                        root,
                        null,
                        false);
            }

            var status =
                await RunGitAsync(
                    root,
                    cancellationToken,
                    "status",
                    "--porcelain=v1",
                    "-z",
                    "--untracked-files=all");

            if (status.ExitCode != 0
                || !string.IsNullOrEmpty(
                    status.Output))
            {
                /*
                    Rollback is enabled only from a clean Git baseline.

                    This guarantees that pre-existing user changes can never
                    be erased by the transaction.
                */
                return
                    new GitTaskTransaction(
                        root,
                        null,
                        false);
            }

            var head =
                await RunGitAsync(
                    root,
                    cancellationToken,
                    "rev-parse",
                    "HEAD");

            if (head.ExitCode != 0
                || string.IsNullOrWhiteSpace(
                    head.Output))
            {
                return
                    new GitTaskTransaction(
                        root,
                        null,
                        false);
            }

            return
                new GitTaskTransaction(
                    root,
                    head.Output.Trim(),
                    true);
        }

        public async Task<bool> HasChangesAsync(
            CancellationToken cancellationToken)
        {
            if (!IsEnabled)
            {
                return false;
            }

            var currentHead =
                await RunGitAsync(
                    _repositoryRoot,
                    cancellationToken,
                    "rev-parse",
                    "HEAD");

            if (currentHead.ExitCode == 0
                && !string.IsNullOrWhiteSpace(
                    _baselineHead)
                && !string.Equals(
                    currentHead.Output.Trim(),
                    _baselineHead,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var status =
                await RunGitAsync(
                    _repositoryRoot,
                    cancellationToken,
                    "status",
                    "--porcelain=v1",
                    "-z",
                    "--untracked-files=all");

            return
                status.ExitCode == 0
                && !string.IsNullOrEmpty(
                    status.Output);
        }

        public async Task<RollbackResult> RollbackAsync(
            CancellationToken cancellationToken)
        {
            if (!IsEnabled)
            {
                return RollbackResult.None;
            }

            if (string.IsNullOrWhiteSpace(
                    _baselineHead))
            {
                return
                    new RollbackResult(
                        true,
                        false,
                        "The pre-task Git commit could not be determined.");
            }

            var currentHead =
                await RunGitAsync(
                    _repositoryRoot,
                    cancellationToken,
                    "rev-parse",
                    "HEAD");

            ProcessResult trackedRestore;

            if (currentHead.ExitCode == 0
                && !string.Equals(
                    currentHead.Output.Trim(),
                    _baselineHead,
                    StringComparison.OrdinalIgnoreCase))
            {
                trackedRestore =
                    await RunGitAsync(
                        _repositoryRoot,
                        cancellationToken,
                        "reset",
                        "--hard",
                        _baselineHead);
            }
            else
            {
                trackedRestore =
                    await RunGitAsync(
                        _repositoryRoot,
                        cancellationToken,
                        "restore",
                        "--staged",
                        "--worktree",
                        "--",
                        ".");
            }

            if (trackedRestore.ExitCode != 0)
            {
                return
                    new RollbackResult(
                        true,
                        false,
                        "Git could not restore tracked files.\n\n" +
                        trackedRestore.Output);
            }

            var untrackedStatus =
                await RunGitAsync(
                    _repositoryRoot,
                    cancellationToken,
                    "status",
                    "--porcelain=v1",
                    "-z",
                    "--untracked-files=all");

            if (untrackedStatus.ExitCode != 0)
            {
                return
                    new RollbackResult(
                        true,
                        false,
                        "Tracked files were restored, but untracked task files could not be inspected.\n\n" +
                        untrackedStatus.Output);
            }

            foreach (var relativePath
                     in ParseUntrackedPaths(
                         untrackedStatus.Output))
            {
                DeleteUntrackedPath(
                    relativePath);
            }

            var finalStatus =
                await RunGitAsync(
                    _repositoryRoot,
                    cancellationToken,
                    "status",
                    "--porcelain=v1",
                    "-z",
                    "--untracked-files=all");

            if (finalStatus.ExitCode != 0)
            {
                return
                    new RollbackResult(
                        true,
                        false,
                        "Rollback ran, but the final Git status could not be verified.\n\n" +
                        finalStatus.Output);
            }

            if (!string.IsNullOrEmpty(
                    finalStatus.Output))
            {
                return
                    new RollbackResult(
                        true,
                        false,
                        "Rollback ran, but Git still reports workspace changes.");
            }

            return
                new RollbackResult(
                    true,
                    true,
                    "Workspace restored.");
        }

        private void DeleteUntrackedPath(
            string relativePath)
        {
            if (string.IsNullOrWhiteSpace(
                    relativePath))
            {
                return;
            }

            var fullPath =
                Path.GetFullPath(
                    Path.Combine(
                        _repositoryRoot,
                        relativePath));

            var root =
                _repositoryRoot.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);

            var rootPrefix =
                root +
                Path.DirectorySeparatorChar;

            if (!fullPath.StartsWith(
                    rootPrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var relative =
                Path.GetRelativePath(
                    root,
                    fullPath);

            if (PathContainsGitDirectory(
                    relative))
            {
                return;
            }

            try
            {
                if (File.Exists(
                        fullPath))
                {
                    File.Delete(
                        fullPath);

                    RemoveEmptyParents(
                        Path.GetDirectoryName(
                            fullPath));
                }
                else if (Directory.Exists(
                             fullPath)
                         && !Directory.EnumerateFileSystemEntries(
                                 fullPath)
                             .Any())
                {
                    Directory.Delete(
                        fullPath);

                    RemoveEmptyParents(
                        Path.GetDirectoryName(
                            fullPath));
                }
            }
            catch
            {
                /*
                    Final Git verification reports rollback as incomplete if
                    something could not be removed.
                */
            }
        }

        private void RemoveEmptyParents(
            string? directory)
        {
            var root =
                _repositoryRoot.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);

            while (!string.IsNullOrWhiteSpace(
                       directory)
                   && !string.Equals(
                       directory,
                       root,
                       StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (!Directory.Exists(
                            directory)
                        || Directory.EnumerateFileSystemEntries(
                                directory)
                            .Any())
                    {
                        return;
                    }

                    var parent =
                        Path.GetDirectoryName(
                            directory);

                    Directory.Delete(
                        directory);

                    directory =
                        parent;
                }
                catch
                {
                    return;
                }
            }
        }

        private static IEnumerable<string> ParseUntrackedPaths(
            string porcelainOutput)
        {
            if (string.IsNullOrEmpty(
                    porcelainOutput))
            {
                yield break;
            }

            var entries =
                porcelainOutput.Split(
                    '\0',
                    StringSplitOptions.RemoveEmptyEntries);

            foreach (var entry
                     in entries)
            {
                if (!entry.StartsWith(
                        "?? ",
                        StringComparison.Ordinal)
                    || entry.Length <= 3)
                {
                    continue;
                }

                yield return
                    entry[3..];
            }
        }

        private static bool PathContainsGitDirectory(
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
                        part.Equals(
                            ".git",
                            StringComparison.OrdinalIgnoreCase));
        }

        private static async Task<ProcessResult> RunGitAsync(
            string workingDirectory,
            CancellationToken cancellationToken,
            params string[] arguments)
        {
            var startInfo =
                new ProcessStartInfo
                {
                    FileName =
                        "git",

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

            startInfo.Environment[
                "GIT_TERMINAL_PROMPT"] =
                "0";

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
                    TryKill(
                        process);

                    throw;
                }
                catch (OperationCanceledException)
                {
                    TryKill(
                        process);

                    return
                        new ProcessResult(
                            -1,
                            "Git operation timed out.");
                }

                var output =
                    await outputTask;

                var error =
                    await errorTask;

                if (!string.IsNullOrWhiteSpace(
                        error))
                {
                    if (!string.IsNullOrWhiteSpace(
                            output))
                    {
                        output +=
                            Environment.NewLine;
                    }

                    output +=
                        error;
                }

                return
                    new ProcessResult(
                        process.ExitCode,
                        output);
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

        private static void TryKill(
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

        private sealed record ProcessResult(
            int ExitCode,
            string Output);
    }

    private sealed record RollbackResult(
        bool Attempted,
        bool Succeeded,
        string Message)
    {
        public static RollbackResult None
        {
            get;
        } =
            new(
                false,
                true,
                string.Empty);
    }
}