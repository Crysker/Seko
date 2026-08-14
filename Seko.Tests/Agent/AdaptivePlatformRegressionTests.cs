using Seko.Core.Workspaces;
using Seko.Infrastructure.Agent.Adaptive;
using Seko.Infrastructure.Agent.Capabilities;
using Seko.Infrastructure.Agent.Capabilities.BuiltIn;
using Seko.Infrastructure.Agent.Permissions;
using Seko.Infrastructure.Agent.Tools;

namespace Seko.Tests.Agent;

public sealed class AdaptivePlatformRegressionTests
{
    [Fact]
    public void BuildContext_IncludesProjectAndSelectedSkill()
    {
        using var scope =
            new TemporaryDirectory();

        File.WriteAllText(
            Path.Combine(
                scope.Path,
                "Example.sln"),
            string.Empty);

        var platform =
            CreatePlatform(
                scope.Path);

        var context =
            platform.BuildContext(
                "Design the settings UI");

        Assert.Contains(
            "Project type: Software",
            context);

        Assert.Contains(
            "UI / UX Design",
            context);
    }

    [Fact]
    public void BuildContext_ReportsMissingDesignAbility()
    {
        using var scope =
            new TemporaryDirectory();

        var platform =
            CreatePlatform(
                scope.Path);

        var context =
            platform.BuildContext(
                "Create this layout in Figma");

        Assert.Contains(
            "design.edit",
            context);
    }

    [Fact]
    public void BuildContext_IsBounded()
    {
        using var scope =
            new TemporaryDirectory();

        var platform =
            CreatePlatform(
                scope.Path);

        var context =
            platform.BuildContext(
                new string(
                    'x',
                    20_000)
                + " ui");

        Assert.True(
            context.Length <= 5_540);
    }

    [Fact]
    public async Task ExtensionSkillGuidance_RequiresExplicitInstructionPermission()
    {
        using var scope =
            new TemporaryDirectory();

        var globalRoot =
            System.IO.Path.Combine(
                scope.Path,
                ".global");

        var extensionRoot =
            System.IO.Path.Combine(
                globalRoot,
                "design-helper");

        Directory.CreateDirectory(
            extensionRoot);

        File.WriteAllText(
            System.IO.Path.Combine(
                extensionRoot,
                "extension.json"),
            """
            {
              "schemaVersion": 1,
              "id": "design-helper",
              "name": "Design Helper",
              "version": "1.0.0",
              "runtime": "declarative-v1",
              "abilities": [],
              "permissions": [],
              "skills": [
                {
                  "id": "brand-helper",
                  "name": "Brand Helper",
                  "triggerTerms": ["brand"],
                  "requiredAbilities": [],
                  "preferredAbilities": [],
                  "instructions": "Use the approved brand workflow.",
                  "priority": 20
                }
              ]
            }
            """);

        var registry =
            new SekoCapabilityRegistry();

        var manager =
            SekoPermissionManager.Load(
                new SekoPermissionStore(
                    System.IO.Path.Combine(
                        scope.Path,
                        ".test",
                        "permissions.json")));

        var platform =
            new SekoAdaptivePlatform(
                new Workspace
                {
                    Id =
                        Guid.NewGuid(),

                    Name =
                        "Test",

                    RootPath =
                        scope.Path
                },
                registry,
                manager,
                globalRoot,
                System.IO.Path.Combine(
                    scope.Path,
                    ".runtime"));

        var before =
            platform.BuildContext(
                "Create the brand page");

        Assert.DoesNotContain(
            "Brand Helper (brand-helper)",
            before);

        await manager.SetDecisionAsync(
            "design-helper",
            CapabilitySource.Extension,
            "agent.instructions",
            PermissionDecision.Allow);

        var after =
            platform.BuildContext(
                "Create the brand page");

        Assert.Contains(
            "Brand Helper (brand-helper)",
            after);
    }

    private static SekoAdaptivePlatform CreatePlatform(
        string root)
    {
        var registry =
            new SekoCapabilityRegistry();

        var tools =
            new SekoToolRegistry();

        registry.Register(
            new WorkspaceCapability(
                Handler(),
                Handler(),
                Handler(),
                Handler(),
                Handler(),
                Handler(),
                Handler(),
                Handler()),
            CapabilitySource.BuiltIn,
            SekoPermissionPolicy.CreateDefault(),
            tools);

        var permissionManager =
            SekoPermissionManager.Load(
                new SekoPermissionStore(
                    System.IO.Path.Combine(
                        root,
                        ".test",
                        "permissions.json")));

        return
            new SekoAdaptivePlatform(
                new Workspace
                {
                    Id =
                        Guid.NewGuid(),

                    Name =
                        "Test",

                    RootPath =
                        root
                },
                registry,
                permissionManager,
                System.IO.Path.Combine(
                    root,
                    ".test",
                    "global-extensions"),
                System.IO.Path.Combine(
                    root,
                    ".test",
                    "extension-runtime"));
    }

    private static SekoToolHandler Handler()
    {
        return
            (_, _) =>
                Task.FromResult(
                    "ok");
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
                    "SekoAdaptiveTests",
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
