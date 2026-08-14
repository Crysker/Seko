using System.Text.Json;
using Seko.Infrastructure.Agent.Extensions;
using Seko.Infrastructure.Agent.Permissions;

namespace Seko.Tests.Agent;

public sealed class ExtensionPlatformRegressionTests
{
    [Fact]
    public void Validator_AcceptsDeclarativeManifest()
    {
        var manifest =
            Manifest(
                "figma");

        var errors =
            new SekoExtensionValidator()
                .Validate(
                    manifest);

        Assert.Empty(
            errors);
    }

    [Fact]
    public void Validator_RejectsInProcessRuntime()
    {
        var manifest =
            Manifest(
                "unsafe");

        manifest.Runtime =
            "dotnet-dll";

        var errors =
            new SekoExtensionValidator()
                .Validate(
                    manifest);

        Assert.Contains(
            errors,
            error =>
                error.Contains(
                    "declarative-v1",
                    StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validator_RejectsProtectedPermission()
    {
        var manifest =
            Manifest(
                "unsafe");

        manifest.Permissions.Add(
            "self.modify.kernel");

        var errors =
            new SekoExtensionValidator()
                .Validate(
                    manifest);

        Assert.Contains(
            errors,
            error =>
                error.Contains(
                    "protected permission",
                    StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Loader_LoadsGlobalAndProjectManifests()
    {
        using var workspace =
            new TemporaryDirectory();

        using var global =
            new TemporaryDirectory();

        WriteManifest(
            Path.Combine(
                global.Path,
                "global-design"),
            Manifest(
                "global-design"));

        var projectRoot =
            Path.Combine(
                workspace.Path,
                ".seko",
                "extensions",
                "project-flow");

        WriteManifest(
            projectRoot,
            Manifest(
                "project-flow"));

        var catalog =
            new SekoExtensionLoader(
                workspace.Path,
                global.Path)
                .Load();

        Assert.Equal(
            2,
            catalog.Packages.Count);

        Assert.Contains(
            catalog.Packages,
            package =>
                package.Source
                    == CapabilitySource.Extension);

        Assert.Contains(
            catalog.Packages,
            package =>
                package.Source
                    == CapabilitySource.Project);
    }

    [Fact]
    public void Loader_RejectsDuplicateIds()
    {
        using var workspace =
            new TemporaryDirectory();

        using var global =
            new TemporaryDirectory();

        WriteManifest(
            Path.Combine(
                global.Path,
                "one"),
            Manifest(
                "same"));

        WriteManifest(
            Path.Combine(
                workspace.Path,
                ".seko",
                "extensions",
                "two"),
            Manifest(
                "same"));

        var catalog =
            new SekoExtensionLoader(
                workspace.Path,
                global.Path)
                .Load();

        Assert.Single(
            catalog.Packages);

        Assert.Single(
            catalog.Issues);
    }

    [Fact]
    public async Task Installer_PreparesAndInstallsDeclarativeCandidate()
    {
        using var scope =
            new TemporaryDirectory();

        var installer =
            new SekoExtensionInstaller(
                scope.Path);

        var candidate =
            await installer.PrepareCandidateAsync(
                Manifest(
                    "figma"),
                new Dictionary<string, string>
                {
                    ["skills/ui.md"] =
                        "Use the design system."
                });

        Assert.True(
            Directory.Exists(
                candidate.RootPath));

        var result =
            installer.Install(
                candidate.RootPath);

        Assert.True(
            Directory.Exists(
                result.InstalledPath));

        Assert.True(
            File.Exists(
                Path.Combine(
                    result.InstalledPath,
                    "extension.json")));
    }

    [Fact]
    public async Task Installer_RejectsSourceOrExecutablePayload()
    {
        using var scope =
            new TemporaryDirectory();

        var installer =
            new SekoExtensionInstaller(
                scope.Path);

        await Assert.ThrowsAsync<InvalidDataException>(
            () =>
                installer.PrepareCandidateAsync(
                    Manifest(
                        "unsafe"),
                    new Dictionary<string, string>
                    {
                        ["adapter.cs"] =
                            "public class Adapter {}"
                    }));
    }

    [Fact]
    public async Task Installer_RejectsPathEscape()
    {
        using var scope =
            new TemporaryDirectory();

        var installer =
            new SekoExtensionInstaller(
                scope.Path);

        await Assert.ThrowsAsync<InvalidDataException>(
            () =>
                installer.PrepareCandidateAsync(
                    Manifest(
                        "escape"),
                    new Dictionary<string, string>
                    {
                        ["../outside.md"] =
                            "no"
                    }));
    }

    [Fact]
    public async Task Installer_UpdateCreatesBackup()
    {
        using var scope =
            new TemporaryDirectory();

        var installer =
            new SekoExtensionInstaller(
                scope.Path);

        var first =
            await installer.PrepareCandidateAsync(
                Manifest(
                    "figma"));

        installer.Install(
            first.RootPath);

        var secondManifest =
            Manifest(
                "figma");

        secondManifest.Version =
            "1.1.0";

        var second =
            await installer.PrepareCandidateAsync(
                secondManifest);

        var result =
            installer.Install(
                second.RootPath);

        Assert.NotNull(
            result.BackupPath);

        Assert.True(
            Directory.Exists(
                result.BackupPath!));
    }

    [Fact]
    public void ManifestSkill_CanRepresentFutureProviderIndependentWorkflow()
    {
        var manifest =
            Manifest(
                "design-workflow");

        manifest.Skills.Add(
            new SekoExtensionSkillManifest
            {
                Id =
                    "brand-design",

                Name =
                    "Brand Design",

                TriggerTerms =
                    new List<string>
                    {
                        "brand"
                    },

                RequiredAbilities =
                    new List<string>
                    {
                        "design.edit"
                    }
            });

        var errors =
            new SekoExtensionValidator()
                .Validate(
                    manifest);

        Assert.Empty(
            errors);
    }

    private static SekoExtensionManifest Manifest(
        string id)
    {
        return
            new SekoExtensionManifest
            {
                Id =
                    id,

                Name =
                    id,

                Version =
                    "1.0.0",

                Runtime =
                    "declarative-v1",

                Abilities =
                    new List<string>
                    {
                        "design.edit"
                    },

                Permissions =
                    new List<string>
                    {
                        "network"
                    }
            };
    }

    private static void WriteManifest(
        string directory,
        SekoExtensionManifest manifest)
    {
        Directory.CreateDirectory(
            directory);

        File.WriteAllText(
            Path.Combine(
                directory,
                "extension.json"),
            JsonSerializer.Serialize(
                manifest));
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
                    "SekoExtensionTests",
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
