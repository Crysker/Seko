namespace Seko.Infrastructure.Agent.Projects;

public sealed record SekoProjectProfile(
    string RootPath,
    string Name,
    string ProjectType,
    IReadOnlyCollection<string> Technologies,
    IReadOnlyCollection<string> RequiredAbilities,
    IReadOnlyCollection<string> PreferredCapabilities,
    IReadOnlyCollection<string> EnabledSkills,
    string? ConfigWarning);
