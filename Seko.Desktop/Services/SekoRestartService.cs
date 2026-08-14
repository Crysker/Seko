using System.Diagnostics;
using System.IO;

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

            var desktopProject =
                Path.Combine(
                    root,
                    "Seko.Desktop",
                    "Seko.Desktop.csproj");

            if (!File.Exists(
                    desktopProject))
            {
                error =
                    "The Seko desktop project could not be found.";

                return false;
            }

            var escapedRoot =
                root.Replace(
                    "'",
                    "''");

            var command =
                $"""
                Wait-Process -Id {currentProcessId} -ErrorAction SilentlyContinue;
                Set-Location -LiteralPath '{escapedRoot}';
                dotnet run --project '.\Seko.Desktop\Seko.Desktop.csproj'
                """;

            var startInfo =
                new ProcessStartInfo
                {
                    FileName =
                        "powershell.exe",

                    UseShellExecute =
                        false,

                    CreateNoWindow =
                        true,

                    WindowStyle =
                        ProcessWindowStyle.Hidden
                };

            startInfo.ArgumentList.Add(
                "-NoLogo");

            startInfo.ArgumentList.Add(
                "-NoProfile");

            startInfo.ArgumentList.Add(
                "-NonInteractive");

            startInfo.ArgumentList.Add(
                "-WindowStyle");

            startInfo.ArgumentList.Add(
                "Hidden");

            startInfo.ArgumentList.Add(
                "-Command");

            startInfo.ArgumentList.Add(
                command);

            var process =
                Process.Start(
                    startInfo);

            if (process is null)
            {
                error =
                    "Windows could not start the Seko restart helper.";

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