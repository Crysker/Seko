using System.Diagnostics;
using System.IO;
using System.Text;

namespace Seko.Desktop.Services;

internal static class SekoRestartService
{
    public static bool TryScheduleRestart(
        string repositoryRoot,
        int currentProcessId,
        out string? error)
    {
        error =
            null;

        try
        {
            var root =
                Path.GetFullPath(
                    repositoryRoot);

            var projectPath =
                Path.Combine(
                    root,
                    "Seko.Desktop",
                    "Seko.Desktop.csproj");

            if (!File.Exists(
                    projectPath))
            {
                error =
                    "Seko.Desktop.csproj could not be found.";

                return false;
            }

            var localAppData =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData);

            var updaterDirectory =
                Path.Combine(
                    localAppData,
                    "Seko",
                    "Updater");

            Directory.CreateDirectory(
                updaterDirectory);

            var scriptPath =
                Path.Combine(
                    updaterDirectory,
                    $"restart-{Guid.NewGuid():N}.ps1");

            var script =
                """
                param(
                    [Parameter(Mandatory=$true)]
                    [int]$OldProcessId,

                    [Parameter(Mandatory=$true)]
                    [string]$RepositoryRoot,

                    [Parameter(Mandatory=$true)]
                    [string]$ScriptPath
                )

                try
                {
                    Wait-Process -Id $OldProcessId -ErrorAction SilentlyContinue

                    Start-Sleep -Milliseconds 350

                    Start-Process `
                        -FilePath "dotnet" `
                        -ArgumentList @(
                            "run",
                            "--project",
                            ".\Seko.Desktop\Seko.Desktop.csproj"
                        ) `
                        -WorkingDirectory $RepositoryRoot `
                        -WindowStyle Hidden
                }
                finally
                {
                    Remove-Item `
                        -LiteralPath $ScriptPath `
                        -Force `
                        -ErrorAction SilentlyContinue
                }
                """;

            File.WriteAllText(
                scriptPath,
                script,
                new UTF8Encoding(false));

            var startInfo =
                new ProcessStartInfo
                {
                    FileName =
                        "powershell.exe",

                    UseShellExecute =
                        false,

                    CreateNoWindow =
                        true
                };

            startInfo.ArgumentList.Add(
                "-NoLogo");

            startInfo.ArgumentList.Add(
                "-NoProfile");

            startInfo.ArgumentList.Add(
                "-NonInteractive");

            startInfo.ArgumentList.Add(
                "-ExecutionPolicy");

            startInfo.ArgumentList.Add(
                "Bypass");

            startInfo.ArgumentList.Add(
                "-File");

            startInfo.ArgumentList.Add(
                scriptPath);

            startInfo.ArgumentList.Add(
                "-OldProcessId");

            startInfo.ArgumentList.Add(
                currentProcessId.ToString());

            startInfo.ArgumentList.Add(
                "-RepositoryRoot");

            startInfo.ArgumentList.Add(
                root);

            startInfo.ArgumentList.Add(
                "-ScriptPath");

            startInfo.ArgumentList.Add(
                scriptPath);

            var helper =
                Process.Start(
                    startInfo);

            if (helper is null)
            {
                error =
                    "Windows could not launch the restart helper.";

                return false;
            }

            return true;
        }
        catch (Exception exception)
        {
            error =
                exception.Message;

            return false;
        }
    }
}