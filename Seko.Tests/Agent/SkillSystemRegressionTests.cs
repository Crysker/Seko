using Seko.Infrastructure.Agent.Capabilities;
using Seko.Infrastructure.Agent.Permissions;
using Seko.Infrastructure.Agent.Projects;
using Seko.Infrastructure.Agent.Skills;
using Seko.Infrastructure.Agent.Tools;

namespace Seko.Tests.Agent;

public sealed class SkillSystemRegressionTests
{
    [Fact]
    public void SkillRegistry_RejectsDuplicateIdsIgnoringCase()
    {
        var registry =
            new SekoSkillRegistry();

        registry.Register(
            Skill(
                "ui"));

        Assert.Throws<InvalidOperationException>(
            () =>
                registry.Register(
                    Skill(
                        "UI")));
    }

    [Fact]
    public void Resolver_SelectsUiSkillForFigmaTask()
    {
        var skills =
            RegistryWithBuiltIns();

        var capabilities =
            CapabilityRegistryWithWorkspace();

        var resolver =
            new SekoSkillResolver();

        var results =
            resolver.Resolve(
                "Create the settings UI in Figma",
                Profile(),
                skills,
                capabilities);

        Assert.Contains(
            results,
            result =>
                result.Skill.Descriptor.Id
                == "ui-ux");
    }

    [Fact]
    public void Resolver_SelectsGameSkillForUnityTask()
    {
        var resolver =
            new SekoSkillResolver();

        var results =
            resolver.Resolve(
                "Fix the Unity player scene",
                Profile(),
                RegistryWithBuiltIns(),
                CapabilityRegistryWithWorkspace());

        Assert.Contains(
            results,
            result =>
                result.Skill.Descriptor.Id
                == "game-development");
    }

    [Fact]
    public void Resolver_ReportsMissingPreferredAbilities()
    {
        var resolver =
            new SekoSkillResolver();

        var result =
            resolver.Resolve(
                    "Design this interface in Figma",
                    Profile(),
                    RegistryWithBuiltIns(),
                    CapabilityRegistryWithWorkspace())
                .Single(
                    item =>
                        item.Skill.Descriptor.Id
                        == "ui-ux");

        Assert.Contains(
            "design.edit",
            result.MissingPreferredAbilities);
    }

    [Fact]
    public void ProjectEnabledSkill_IsSelectedWithoutTriggerTerm()
    {
        var resolver =
            new SekoSkillResolver();

        var project =
            Profile(
                enabledSkills:
                    new[]
                    {
                        "research"
                    });

        var results =
            resolver.Resolve(
                "Please continue",
                project,
                RegistryWithBuiltIns(),
                CapabilityRegistryWithWorkspace());

        Assert.Contains(
            results,
            result =>
                result.Skill.Descriptor.Id
                == "research");
    }

    [Fact]
    public void SkillDescriptor_RejectsDuplicateTriggerTerms()
    {
        Assert.Throws<ArgumentException>(
            () =>
                new SekoSkillDescriptor(
                    "bad",
                    "Bad",
                    string.Empty,
                    new[]
                    {
                        "same",
                        "SAME"
                    },
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    string.Empty));
    }

    private static SekoSkillRegistry RegistryWithBuiltIns()
    {
        var registry =
            new SekoSkillRegistry();

        foreach (var skill
                 in SekoBuiltInSkills.CreateAll())
        {
            registry.Register(
                skill);
        }

        return registry;
    }

    private static SekoCapabilityRegistry CapabilityRegistryWithWorkspace()
    {
        var toolRegistry =
            new SekoToolRegistry();

        var registry =
            new SekoCapabilityRegistry();

        registry.Register(
            new TestCapability(),
            CapabilitySource.BuiltIn,
            SekoPermissionPolicy.CreateDefault(),
            toolRegistry);

        return registry;
    }

    private static SekoProjectProfile Profile(
        IEnumerable<string>? enabledSkills = null)
    {
        return
            new SekoProjectProfile(
                @"C:\project",
                "Project",
                "Software",
                new[]
                {
                    ".NET"
                },
                Array.Empty<string>(),
                Array.Empty<string>(),
                (enabledSkills
                 ?? Array.Empty<string>())
                    .ToList()
                    .AsReadOnly(),
                null);
    }

    private static ISekoSkill Skill(
        string id)
    {
        return
            new DeclarativeSekoSkill(
                new SekoSkillDescriptor(
                    id,
                    id,
                    string.Empty,
                    new[]
                    {
                        "trigger"
                    },
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    string.Empty));
    }

    private sealed class TestCapability :
        ISekoCapability
    {
        public CapabilityDescriptor Descriptor
        {
            get;
        } =
            new(
                "test.workspace",
                "Workspace",
                string.Empty,
                new[]
                {
                    "filesystem.read",
                    "filesystem.write"
                });

        public IReadOnlyCollection<SekoToolRegistration> Tools
        {
            get;
        } =
            Array.Empty<SekoToolRegistration>();
    }
}
