using System.Text.Json;
using Seko.Infrastructure.Agent.Projects;

namespace Seko.Tests.Agent;

public sealed class ProjectIntelligenceRegressionTests
{
    [Fact]
    public void Detect_FindsDotNetProject()
    {
        using var scope =
            new TemporaryDirectory();

        File.WriteAllText(
            Path.Combine(
                scope.Path,
                "Example.sln"),
            string.Empty);

        var profile =
            new SekoProjectDetector()
                .Detect(
                    scope.Path);

        Assert.Contains(
            ".NET",
            profile.Technologies);

        Assert.Contains(
            "project.build",
            profile.RequiredAbilities);

        Assert.Equal(
            "Software",
            profile.ProjectType);
    }

    [Fact]
    public void Detect_FindsUnityProject()
    {
        using var scope =
            new TemporaryDirectory();

        Directory.CreateDirectory(
            Path.Combine(
                scope.Path,
                "Assets"));

        Directory.CreateDirectory(
            Path.Combine(
                scope.Path,
                "ProjectSettings"));

        File.WriteAllText(
            Path.Combine(
                scope.Path,
                "ProjectSettings",
                "ProjectVersion.txt"),
            "m_EditorVersion: test");

        var profile =
            new SekoProjectDetector()
                .Detect(
                    scope.Path);

        Assert.Contains(
            "Unity",
            profile.Technologies);

        Assert.Equal(
            "Game",
            profile.ProjectType);

        Assert.Contains(
            "engine.unity",
            profile.PreferredCapabilities);
    }

    [Fact]
    public void Detect_MergesSekoProjectConfig()
    {
        using var scope =
            new TemporaryDirectory();

        var seko =
            Path.Combine(
                scope.Path,
                ".seko");

        Directory.CreateDirectory(
            seko);

        var config =
            new SekoProjectConfig
            {
                Name =
                    "Custom Project",

                Type =
                    "Product Design",

                Technologies =
                    new List<string>
                    {
                        "Figma"
                    },

                RequiredAbilities =
                    new List<string>
                    {
                        "design.edit"
                    },

                PreferredCapabilities =
                    new List<string>
                    {
                        "figma"
                    },

                EnabledSkills =
                    new List<string>
                    {
                        "ui-ux"
                    }
            };

        File.WriteAllText(
            Path.Combine(
                seko,
                "project.json"),
            JsonSerializer.Serialize(
                config));

        var profile =
            new SekoProjectDetector()
                .Detect(
                    scope.Path);

        Assert.Equal(
            "Custom Project",
            profile.Name);

        Assert.Equal(
            "Product Design",
            profile.ProjectType);

        Assert.Contains(
            "Figma",
            profile.Technologies);

        Assert.Contains(
            "design.edit",
            profile.RequiredAbilities);

        Assert.Contains(
            "ui-ux",
            profile.EnabledSkills);
    }

    [Fact]
    public void Detect_CorruptProjectConfigDoesNotCrash()
    {
        using var scope =
            new TemporaryDirectory();

        var seko =
            Path.Combine(
                scope.Path,
                ".seko");

        Directory.CreateDirectory(
            seko);

        File.WriteAllText(
            Path.Combine(
                seko,
                "project.json"),
            "{ invalid");

        var profile =
            new SekoProjectDetector()
                .Detect(
                    scope.Path);

        Assert.NotNull(
            profile.ConfigWarning);

        Assert.Contains(
            "filesystem.read",
            profile.RequiredAbilities);
    }

    [Fact]
    public void Detect_FindsGitRepository()
    {
        using var scope =
            new TemporaryDirectory();

        Directory.CreateDirectory(
            Path.Combine(
                scope.Path,
                ".git"));

        var profile =
            new SekoProjectDetector()
                .Detect(
                    scope.Path);

        Assert.Contains(
            "Git",
            profile.Technologies);

        Assert.Contains(
            "source-control.git",
            profile.PreferredCapabilities);
    }

    private sealed class TemporaryDirectory :
        IDisposable
    {
        public string Path
        {
            get;
        }

        public TemporaryDirectory()
        {
            Path =
                System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    "SekoProjectTests",
                    Guid.NewGuid().ToString(
                        "N"));

            Directory.CreateDirectory(
                Path);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(
                        Path))
                {
                    Directory.Delete(
                        Path,
                        true);
                }
            }
            catch
            {
            }
        }
    }
}
