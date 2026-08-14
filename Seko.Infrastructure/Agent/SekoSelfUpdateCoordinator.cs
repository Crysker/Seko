using System.Diagnostics;
using Seko.Core.Agent;
using Seko.Core.Workspaces;

namespace Seko.Infrastructure.Agent;

internal sealed record SekoSelfUpdateResult(
    bool CommitDetected,
    bool PushSucceeded,
    bool ShouldRestart,
    string Message);

internal static class SekoSelfUpdateCoordinator
{
    private static readonly HashSet<string> RestartExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".cs",
            ".xaml",
            ".csproj",
            ".sln",
            ".props",
            ".targets"
        };

    public static bool IsSekoRepository(
        Workspace workspace)
    {
        var root =
            Path.GetFullPath(
                workspace.RootPath);

        return
            File.Exists(
                Path.Combine(
                    root,
                    "Seko.sln"))

            && File.Exists(
                Path.Combine(
                    root,
                    "Seko.Desktop",
                    "Seko.Desktop.csproj"))

            && Directory.Exists(
                Path.Combine(
                    root,
                    "Seko.Core"))

            && Directory.Exists(
                Path.Combine(
                    root,
                    "Seko.Infrastructure"));
    }

    public static async Task<string?> GetHeadAsync(
        Workspace workspace,
        CancellationToken cancellationToken = default)
    {
        if (!IsSekoRepository(
                workspace))
        {
            return null;
        }

        var result =
            await RunProcessAsync(
                "git",
                new[]
                {
                    "rev-parse",
                    "HEAD"
                },
                workspace.RootPath,
                cancellationToken);

        if (result.ExitCode != 0)
        {
            return null;
        }

        var head =
            result.Output.Trim();

        return
            string.IsNullOrWhiteSpace(
                head)
                ? null
                : head;
    }

    public static async Task<SekoSelfUpdateResult> FinalizeAsync(
        Workspace workspace,
        string? beforeHead,
        Action<AgentActivity>? report,
        CancellationToken cancellationToken = default)
    {
        if (!IsSekoRepository(
                workspace))
        {
            return
                new SekoSelfUpdateResult(
                    false,
                    false,
                    false,
                    string.Empty);
        }

        if (string.IsNullOrWhiteSpace(
                beforeHead))
        {
            return
                new SekoSelfUpdateResult(
                    false,
                    false,
                    false,
                    string.Empty);
        }

        var afterHead =
            await GetHeadAsync(
                workspace,
                cancellationToken);

        if (string.IsNullOrWhiteSpace(
                afterHead)
            || string.Equals(
                beforeHead,
                afterHead,
                StringComparison.OrdinalIgnoreCase))
        {
            return
                new SekoSelfUpdateResult(
                    false,
                    false,
                    false,
                    string.Empty);
        }

        var statusResult =
            await RunProcessAsync(
                "git",
                new[]
                {
                    "status",
                    "--porcelain"
                },
                workspace.RootPath,
                cancellationToken);

        if (statusResult.ExitCode != 0)
        {
            return
                new SekoSelfUpdateResult(
                    true,
                    false,
                    false,
                    "Self-update: Git status could not be verified, so automatic push and restart were skipped.");
        }

        if (!string.IsNullOrWhiteSpace(
                statusResult.Output))
        {
            report?.Invoke(
                new AgentActivity(
                    AgentActivityKind.Error,
                    "Uncommitted files remain. Push/restart skipped."));

            return
                new SekoSelfUpdateResult(
                    true,
                    false,
                    false,
                    "Self-update: a new commit exists, but additional uncommitted changes remain. Automatic push and restart were skipped for safety.");
        }

        var changedFilesResult =
            await RunProcessAsync(
                "git",
                new[]
                {
                    "diff",
                    "--name-only",
                    beforeHead,
                    afterHead
                },
                workspace.RootPath,
                cancellationToken);

        var shouldRestart =
            changedFilesResult.ExitCode == 0
            && changedFilesResult.Output
                .Split(
                    new[]
                    {
                        '\r',
                        '\n'
                    },
                    StringSplitOptions.RemoveEmptyEntries)
                .Any(
                    path =>
                        RestartExtensions.Contains(
                            Path.GetExtension(
                                path)));

        report?.Invoke(
            new AgentActivity(
                AgentActivityKind.Git,
                "Pushing to GitHub…"));

        var pushResult =
            await RunProcessAsync(
                "git",
                new[]
                {
                    "push"
                },
                workspace.RootPath,
                cancellationToken,
                TimeSpan.FromMinutes(2));

        if (pushResult.ExitCode == 0)
        {
            report?.Invoke(
                new AgentActivity(
                    AgentActivityKind.Git,
                    "Push complete."));

            if (shouldRestart)
            {
                report?.Invoke(
                    new AgentActivity(
                        AgentActivityKind.Completed,
                        "Update ready. Restarting…"));
            }

            return
                new SekoSelfUpdateResult(
                    true,
                    true,
                    shouldRestart,
                    shouldRestart
                        ? $"GitHub: pushed {ShortHash(afterHead)} successfully.\nUpdate: restart scheduled."
                        : $"GitHub: pushed {ShortHash(afterHead)} successfully.");
        }

        report?.Invoke(
            new AgentActivity(
                AgentActivityKind.Error,
                "GitHub push failed. Local commit is safe."));

        return
            new SekoSelfUpdateResult(
                true,
                false,
                shouldRestart,
                "GitHub: automatic push failed. The local commit is still safe.\n\n" +
                pushResult.Output +
                (shouldRestart
                    ? "\n\nUpdate: restarting into the local build anyway."
                    : string.Empty));
    }

    private static string ShortHash(
        string hash)
    {
        return
            hash.Length <= 8
                ? hash
                : hash[..8];
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

        startInfo.Environment[
            "GIT_TERMINAL_PROMPT"] =
            "0";

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