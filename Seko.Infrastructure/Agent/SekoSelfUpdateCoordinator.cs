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

        /*
            A new Git commit was created during this Seko task.

            Because this coordinator only runs for Seko's own verified
            repository, a new self-update commit is enough to request a restart.

            This deliberately avoids false negatives caused by trying to infer
            whether a changed extension requires a restart.

            Restarting after a small non-runtime self-update is harmless.
            Failing to restart after a real source/UI update is not.
        */
        const bool shouldRestart =
            true;

        report?.Invoke(
            new AgentActivity(
                AgentActivityKind.Git,
                "Self-update commit detected."));

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
            report?.Invoke(
                new AgentActivity(
                    AgentActivityKind.Error,
                    "Git status could not be verified."));

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

        report?.Invoke(
            new AgentActivity(
                AgentActivityKind.Git,
                "Pushing to GitHub..."));

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

            report?.Invoke(
                new AgentActivity(
                    AgentActivityKind.Completed,
                    "Update ready. Restarting..."));

            return
                new SekoSelfUpdateResult(
                    true,
                    true,
                    shouldRestart,
                    $"GitHub: pushed {ShortHash(afterHead)} successfully.\n" +
                    "Update: restart requested.");
        }

        /*
            The local commit is already safe.

            A GitHub/network failure should not prevent the locally updated
            application from loading its new source.
        */
        report?.Invoke(
            new AgentActivity(
                AgentActivityKind.Error,
                "GitHub push failed. Local commit is safe."));

        report?.Invoke(
            new AgentActivity(
                AgentActivityKind.Completed,
                "Restarting into the local update..."));

        return
            new SekoSelfUpdateResult(
                true,
                false,
                shouldRestart,
                "GitHub: automatic push failed. The local commit is still safe.\n\n" +
                pushResult.Output +
                "\n\nUpdate: restarting into the local build anyway.");
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