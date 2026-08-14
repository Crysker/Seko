using System.Diagnostics;
using Seko.Infrastructure.Agent.Git;

namespace Seko.Tests.Agent;

public sealed class GitServiceRegressionTests
{
    [Fact]
    public async Task RepositoryState_NonRepository_IsReported()
    {
        using var workspace =
            new TemporaryDirectory();

        var service =
            new GitService(
                workspace.RootPath);

        var state =
            await service.GetRepositoryStateAsync();

        Assert.False(
            state.IsRepository);

        Assert.True(
            state.IsClean);
    }

    [Fact]
    public async Task RepositoryState_CleanRepository_IsReported()
    {
        using var repository =
            new TemporaryGitRepository();

        var service =
            new GitService(
                repository.RootPath);

        var state =
            await service.GetRepositoryStateAsync();

        Assert.True(
            state.IsRepository);

        Assert.True(
            state.IsClean);
    }

    [Fact]
    public async Task RepositoryState_DirtyRepository_IsReported()
    {
        using var repository =
            new TemporaryGitRepository();

        repository.WriteFile(
            "dirty.txt",
            "dirty");

        var service =
            new GitService(
                repository.RootPath);

        var state =
            await service.GetRepositoryStateAsync();

        Assert.True(
            state.IsRepository);

        Assert.False(
            state.IsClean);
    }

    [Fact]
    public async Task GetStatusAsync_ReturnsChangedFile()
    {
        using var repository =
            new TemporaryGitRepository();

        repository.WriteFile(
            "changed.txt",
            "changed");

        var service =
            new GitService(
                repository.RootPath);

        var result =
            await service.GetStatusAsync();

        Assert.True(
            result.Succeeded);

        Assert.Contains(
            "changed.txt",
            result.Output);
    }

    [Fact]
    public async Task GetDiffAsync_ReturnsTrackedModification()
    {
        using var repository =
            new TemporaryGitRepository();

        repository.CreateTrackedFile(
            "tracked.txt",
            "before");

        repository.WriteFile(
            "tracked.txt",
            "after");

        var service =
            new GitService(
                repository.RootPath);

        var result =
            await service.GetDiffAsync();

        Assert.True(
            result.Succeeded);

        Assert.Contains(
            "tracked.txt",
            result.Output);

        Assert.Contains(
            "-before",
            result.Output);

        Assert.Contains(
            "+after",
            result.Output);
    }

    [Fact]
    public async Task CommitAsync_StagesAndCommitsRequestedFile()
    {
        using var repository =
            new TemporaryGitRepository();

        repository.WriteFile(
            "feature.txt",
            "feature");

        var service =
            new GitService(
                repository.RootPath);

        var result =
            await service.CommitAsync(
                new[]
                {
                    "feature.txt"
                },
                "Add feature");

        Assert.True(
            result.Succeeded);

        Assert.Equal(
            "Seko: Add feature",
            result.CommitMessage);

        Assert.False(
            string.IsNullOrWhiteSpace(
                result.ShortHash));

        var status =
            await service.GetStatusAsync();

        Assert.True(
            status.Succeeded);

        Assert.True(
            string.IsNullOrWhiteSpace(
                status.Output));
    }

    [Fact]
    public async Task CommitAsync_NoEffectiveChanges_IsReported()
    {
        using var repository =
            new TemporaryGitRepository();

        repository.CreateTrackedFile(
            "stable.txt",
            "stable");

        var service =
            new GitService(
                repository.RootPath);

        var result =
            await service.CommitAsync(
                new[]
                {
                    "stable.txt"
                },
                "No effective change");

        Assert.True(
            result.StagingSucceeded);

        Assert.False(
            result.HasChanges);

        Assert.False(
            result.CommitSucceeded);
    }

    [Fact]
    public void CreateCommitMessage_UsesFirstLineAndRemovesQuotes()
    {
        var message =
            GitService.CreateCommitMessage(
                "Fix \"quoted\" behavior\r\nIgnore this second line");

        Assert.Equal(
            "Seko: Fix quoted behavior",
            message);
    }

    [Fact]
    public void CreateCommitMessage_TruncatesLongFirstLine()
    {
        var request =
            new string(
                'x',
                100);

        var message =
            GitService.CreateCommitMessage(
                request);

        Assert.StartsWith(
            "Seko: ",
            message);

        Assert.EndsWith(
            "...",
            message);

        Assert.True(
            message.Length <= 69);
    }

    private class TemporaryDirectory :
        IDisposable
    {
        public string RootPath
        {
            get;
        }

        public TemporaryDirectory()
        {
            RootPath =
                Path.Combine(
                    Path.GetTempPath(),
                    "Seko.Tests",
                    Guid.NewGuid()
                        .ToString("N"));

            Directory.CreateDirectory(
                RootPath);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(
                        RootPath))
                {
                    Directory.Delete(
                        RootPath,
                        true);
                }
            }
            catch
            {
                // Cleanup must not hide the real test result.
            }
        }
    }

    private sealed class TemporaryGitRepository :
        TemporaryDirectory
    {
        public TemporaryGitRepository()
        {
            RunGit(
                "init");

            RunGit(
                "config",
                "user.name",
                "Seko Tests");

            RunGit(
                "config",
                "user.email",
                "seko-tests@example.invalid");
        }

        public void WriteFile(
            string relativePath,
            string content)
        {
            var fullPath =
                Path.Combine(
                    RootPath,
                    relativePath);

            var directory =
                Path.GetDirectoryName(
                    fullPath);

            if (!string.IsNullOrWhiteSpace(
                    directory))
            {
                Directory.CreateDirectory(
                    directory);
            }

            File.WriteAllText(
                fullPath,
                content);
        }

        public void CreateTrackedFile(
            string relativePath,
            string content)
        {
            WriteFile(
                relativePath,
                content);

            RunGit(
                "add",
                "--",
                relativePath);

            RunGit(
                "commit",
                "-m",
                "Initial");
        }

        private void RunGit(
            params string[] arguments)
        {
            var startInfo =
                new ProcessStartInfo
                {
                    FileName =
                        "git",

                    WorkingDirectory =
                        RootPath,

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

            using var process =
                Process.Start(
                    startInfo)
                ?? throw new InvalidOperationException(
                    "Could not start Git.");

            var output =
                process.StandardOutput.ReadToEnd();

            var error =
                process.StandardError.ReadToEnd();

            process.WaitForExit();

            if (process.ExitCode == 0)
            {
                return;
            }

            throw new InvalidOperationException(
                $"Git command failed: git {string.Join(" ", arguments)}\n"
                + output
                + Environment.NewLine
                + error);
        }
    }
}
