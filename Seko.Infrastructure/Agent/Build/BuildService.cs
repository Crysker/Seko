using System.Diagnostics;
using Seko.Core.Workspaces;
using Seko.Infrastructure.Agent.Safety;

namespace Seko.Infrastructure.Agent.Build;

public sealed class BuildService
{
    private readonly Workspace _workspace;
    private readonly WorkspacePathGuard _pathGuard;
    private readonly string _workspaceRoot;

    public BuildService(
        Workspace workspace,
        WorkspacePathGuard pathGuard)
    {
        _workspace =
            workspace
            ?? throw new ArgumentNullException(
                nameof(workspace));

        _pathGuard =
            pathGuard
            ?? throw new ArgumentNullException(
                nameof(pathGuard));

        _workspaceRoot =
            _pathGuard.WorkspaceRoot;
    }

    public string? FindBuildTarget()
    {
        var solution =
            Directory
                .EnumerateFiles(
                    _workspaceRoot,
                    "*.sln",
                    SearchOption.TopDirectoryOnly)
                .Where(
                    path =>
                        !_pathGuard.IsReparsePoint(
                            path))
                .OrderBy(
                    path => path,
                    StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

        if (solution is not null)
        {
            return solution;
        }

        return
            _pathGuard
                .EnumerateWorkspaceFiles(
                    10_000)
                .Where(
                    path =>
                        Path.GetExtension(
                                path)
                            .Equals(
                                ".csproj",
                                StringComparison.OrdinalIgnoreCase))
                .OrderBy(
                    path => path,
                    StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
    }

    public async Task<BuildResult> BuildAsync(
        CancellationToken cancellationToken = default)
    {
        var target =
            FindBuildTarget();

        if (target is null)
        {
            return
                new BuildResult(
                    null,
                    -1,
                    "No .sln or .csproj file was found in this workspace.");
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
                    $"-p:BaseOutputPath={buildOutput}{Path.DirectorySeparatorChar}"
                },
                _workspaceRoot,
                cancellationToken,
                TimeSpan.FromMinutes(3));

        return
            new BuildResult(
                target,
                result.ExitCode,
                result.Output);
    }

    public async Task<BuildResult> TestAsync(
        CancellationToken cancellationToken = default)
    {
        var target =
            FindBuildTarget();

        if (target is null)
        {
            return
                new BuildResult(
                    null,
                    -1,
                    "No .sln or .csproj file was found in this workspace.");
        }

        var result =
            await RunProcessAsync(
                "dotnet",
                new[]
                {
                    "test",
                    target,
                    "-c",
                    "Release",
                    "--nologo"
                },
                _workspaceRoot,
                cancellationToken,
                TimeSpan.FromMinutes(5));

        return
            new BuildResult(
                target,
                result.ExitCode,
                result.Output);
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
                timeout);

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

    private sealed record ProcessResult(
        int ExitCode,
        string Output);
}
