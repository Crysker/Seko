using System.Text;
using Seko.Core.Workspaces;
using Seko.Infrastructure.Agent.Capabilities;
using Seko.Infrastructure.Agent.Extensions;
using Seko.Infrastructure.Agent.Permissions;
using Seko.Infrastructure.Agent.Skills;
using Seko.Infrastructure.Agent.Projects;

namespace Seko.Infrastructure.Agent.Adaptive;

public sealed class SekoAdaptivePlatform
{
    private const int MaximumContextCharacters =
        5_500;

    private readonly Workspace _workspace;
    private readonly SekoCapabilityRegistry _capabilityRegistry;
    private readonly SekoPermissionManager _permissionManager;
    private readonly SekoProjectDetector _projectDetector =
        new();

    private readonly SekoSkillResolver _skillResolver =
        new();

    private readonly SekoExtensionLoader _extensionLoader;

    public SekoExtensionInstaller ExtensionInstaller
    {
        get;
    }

    public SekoProjectProfile ProjectProfile
    {
        get;
        private set;
    }

    public SekoExtensionCatalog ExtensionCatalog
    {
        get;
        private set;
    }

    public SekoSkillRegistry SkillRegistry
    {
        get;
        private set;
    }

    public SekoAdaptivePlatform(
        Workspace workspace,
        SekoCapabilityRegistry capabilityRegistry,
        SekoPermissionManager permissionManager,
        string? globalExtensionRoot = null,
        string? extensionInstallRoot = null)
    {
        _workspace =
            workspace
            ?? throw new ArgumentNullException(
                nameof(workspace));

        _capabilityRegistry =
            capabilityRegistry
            ?? throw new ArgumentNullException(
                nameof(capabilityRegistry));

        _permissionManager =
            permissionManager
            ?? throw new ArgumentNullException(
                nameof(permissionManager));

        _extensionLoader =
            new SekoExtensionLoader(
                workspace.RootPath,
                globalExtensionRoot);

        ExtensionInstaller =
            new SekoExtensionInstaller(
                extensionInstallRoot);

        ProjectProfile =
            _projectDetector.Detect(
                workspace.RootPath);

        ExtensionCatalog =
            _extensionLoader.Load();

        SkillRegistry =
            BuildSkillRegistry(
                ExtensionCatalog);
    }

    public void Refresh()
    {
        ProjectProfile =
            _projectDetector.Detect(
                _workspace.RootPath);

        ExtensionCatalog =
            _extensionLoader.Load();

        SkillRegistry =
            BuildSkillRegistry(
                ExtensionCatalog);
    }

    public string BuildContext(
        string task)
    {
        Refresh();

        var resolutions =
            _skillResolver.Resolve(
                task,
                ProjectProfile,
                SkillRegistry,
                _capabilityRegistry);

        var builder =
            new StringBuilder();

        builder.AppendLine(
            $"Project: {CollapseWhitespace(ProjectProfile.Name)}");

        builder.AppendLine(
            $"Project type: {CollapseWhitespace(ProjectProfile.ProjectType)}");

        builder.AppendLine(
            "Detected technologies: "
            + JoinOrNone(
                ProjectProfile.Technologies));

        builder.AppendLine(
            "Active abilities: "
            + JoinOrNone(
                _capabilityRegistry.ActiveAbilities));

        var inactiveKnown =
            _capabilityRegistry.KnownAbilities
                .Except(
                    _capabilityRegistry.ActiveAbilities,
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

        builder.AppendLine(
            "Known but inactive abilities: "
            + JoinOrNone(
                inactiveKnown));

        if (ProjectProfile.RequiredAbilities.Count > 0)
        {
            builder.AppendLine(
                "Project-required abilities: "
                + JoinOrNone(
                    ProjectProfile.RequiredAbilities));
        }

        if (ProjectProfile.PreferredCapabilities.Count > 0)
        {
            builder.AppendLine(
                "Project-preferred capability providers: "
                + JoinOrNone(
                    ProjectProfile.PreferredCapabilities));
        }

        var configWarning =
            ProjectProfile.ConfigWarning;

        if (!string.IsNullOrWhiteSpace(
                configWarning))
        {
            builder.AppendLine(
                "Project config warning: "
                + CollapseWhitespace(
                    configWarning));
        }

        if (ExtensionCatalog.Packages.Count > 0)
        {
            builder.AppendLine(
                "Discovered extension manifests:");

            foreach (var package
                     in ExtensionCatalog.Packages.Take(
                         8))
            {
                builder.AppendLine(
                    $"- {package.Manifest.Id} {package.Manifest.Version} "
                    + $"[{package.Source}; {package.Manifest.Runtime}]");

                if (package.Manifest.Abilities.Count > 0)
                {
                    builder.AppendLine(
                        "  Declared extension abilities (not active until a safe runtime provider is connected): "
                        + string.Join(
                            ", ",
                            package.Manifest.Abilities
                                .Take(
                                    12)
                                .Select(
                                    CollapseWhitespace)));
                }

                if (package.Manifest.Permissions.Count > 0)
                {
                    builder.AppendLine(
                        "  Declared future runtime permissions: "
                        + string.Join(
                            ", ",
                            package.Manifest.Permissions
                                .Take(
                                    12)
                                .Select(
                                    CollapseWhitespace)));
                }

                if (package.Manifest.Skills.Count > 0)
                {
                    var instructionDecision =
                        _permissionManager.Policy.Evaluate(
                            package.Manifest.Id,
                            package.Source,
                            "agent.instructions");

                    builder.AppendLine(
                        $"  Extension skill guidance permission: {instructionDecision}");
                }
            }
        }

        if (ExtensionCatalog.Issues.Count > 0)
        {
            builder.AppendLine(
                $"Extension load issues: {ExtensionCatalog.Issues.Count}. Invalid extensions are ignored.");
        }

        if (resolutions.Count > 0)
        {
            builder.AppendLine(
                "Selected skills:");

            foreach (var resolution
                     in resolutions)
            {
                var descriptor =
                    resolution.Skill.Descriptor;

                builder.AppendLine(
                    $"- {CollapseWhitespace(descriptor.Name)} ({descriptor.Id})");

                if (!string.IsNullOrWhiteSpace(
                        descriptor.Instructions))
                {
                    builder.AppendLine(
                        "  Guidance: "
                        + CollapseWhitespace(
                            descriptor.Instructions));
                }

                if (resolution.MissingRequiredAbilities.Count > 0)
                {
                    builder.AppendLine(
                        "  Missing required abilities: "
                        + JoinOrNone(
                            resolution.MissingRequiredAbilities));
                }

                if (resolution.MissingPreferredAbilities.Count > 0)
                {
                    builder.AppendLine(
                        "  Useful abilities not currently active: "
                        + JoinOrNone(
                            resolution.MissingPreferredAbilities));
                }
            }
        }
        else
        {
            builder.AppendLine(
                "Selected skills: none; use the general agent behavior.");
        }

        var context =
            builder
                .ToString()
                .Trim();

        if (context.Length
            <= MaximumContextCharacters)
        {
            return context;
        }

        return
            context[..MaximumContextCharacters]
            + "\n[adaptive context truncated]";
    }

    private SekoSkillRegistry BuildSkillRegistry(
        SekoExtensionCatalog catalog)
    {
        var registry =
            new SekoSkillRegistry();

        foreach (var skill
                 in SekoBuiltInSkills.CreateAll())
        {
            registry.Register(
                skill);
        }

        foreach (var package
                 in catalog.Packages)
        {
            if (package.Manifest.Skills.Count == 0)
            {
                continue;
            }

            var instructionDecision =
                _permissionManager.Policy.Evaluate(
                    package.Manifest.Id,
                    package.Source,
                    "agent.instructions");

            if (instructionDecision
                != PermissionDecision.Allow)
            {
                continue;
            }

            foreach (var skillManifest
                     in package.Manifest.Skills)
            {
                var skill =
                    new DeclarativeSekoSkill(
                        new SekoSkillDescriptor(
                            skillManifest.Id,
                            skillManifest.Name,
                            skillManifest.Description,
                            skillManifest.TriggerTerms,
                            skillManifest.RequiredAbilities,
                            skillManifest.PreferredAbilities,
                            skillManifest.Instructions,
                            skillManifest.Priority));

                registry.TryRegister(
                    skill);
            }
        }

        return registry;
    }

    private static string JoinOrNone(
        IEnumerable<string> values)
    {
        var materialized =
            values
                .Take(
                    30)
                .ToList();

        return
            materialized.Count == 0
                ? "none"
                : string.Join(
                    ", ",
                    materialized.Select(
                        CollapseWhitespace));
    }

    private static string CollapseWhitespace(
        string value)
    {
        return
            string.Join(
                " ",
                value.Split(
                    (char[]?)null,
                    StringSplitOptions.RemoveEmptyEntries));
    }
}
