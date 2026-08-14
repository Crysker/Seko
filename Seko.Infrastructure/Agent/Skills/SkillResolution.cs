namespace Seko.Infrastructure.Agent.Skills;

public sealed record SkillResolution(
    ISekoSkill Skill,
    int Score,
    IReadOnlyCollection<string> MissingRequiredAbilities,
    IReadOnlyCollection<string> MissingPreferredAbilities);
