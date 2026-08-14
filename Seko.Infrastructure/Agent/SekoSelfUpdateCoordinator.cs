using System.Diagnostics;
using Seko.Core.Workspaces;

namespace Seko.Infrastructure.Agent;

internal sealed record SekoSelfUpdateResult(
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

    public static async Task<SekoSelfUpdateResult> PushAsync(
        Workspace workspace,
        CancellationToken cancellationToken = default)
    {
        if (!IsSekoRepository(
                workspace))
        {
            return
                new SekoSelfUpdateResult(
                    false,
                    string.Empty);
        }

        var root =
            Path.GetFullPath(
                workspace.RootPath);

        var pushResult =
            await RunProcessAsync(
                "git",
                new[]
                {
                    "push"
                },
                root,
                cancellationToken,
                TimeSpan.FromMinutes(2));

        if (pushResult.ExitCode == 0)
        {
            return
                new SekoSelfUpdateResult(
                    true,
                    "GitHub: pushed successfully.\n" +
                    "Update: restarting Seko to load the new build.");
        }

        return
            new SekoSelfUpdateResult(
                true,
                "GitHub: automatic push failed, but the local commit is safe.\n\n" +
                pushResult.Output +
                "\n\nUpdate: restarting Seko with the local update anyway.");
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string executable,
        IEnumerable<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken,
        TimeSpan timeout)
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
                timeout);

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
                        "Git push timed out.");
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
        catch (Exception exception)
        {
            return
                new ProcessResult(
                    -1,
                    exception.Message);
        }
    }

    private sealed record ProcessResult(
        int ExitCode,
        string Output);
}