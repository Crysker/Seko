namespace Seko.Infrastructure.Agent.Skills;

public sealed class SekoSkillRegistry
{
    private readonly Dictionary<string, ISekoSkill> _skills =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<ISekoSkill> Skills =>
        _skills.Values;

    public void Register(
        ISekoSkill skill)
    {
        ArgumentNullException.ThrowIfNull(
            skill);

        var descriptor =
            skill.Descriptor
            ?? throw new InvalidOperationException(
                "Skill descriptor cannot be null.");

        if (!_skills.TryAdd(
                descriptor.Id,
                skill))
        {
            throw new InvalidOperationException(
                $"Skill '{descriptor.Id}' is already registered.");
        }
    }

    public bool TryRegister(
        ISekoSkill skill)
    {
        ArgumentNullException.ThrowIfNull(
            skill);

        var descriptor =
            skill.Descriptor
            ?? throw new InvalidOperationException(
                "Skill descriptor cannot be null.");

        return
            _skills.TryAdd(
                descriptor.Id,
                skill);
    }

    public ISekoSkill? FindById(
        string skillId)
    {
        if (string.IsNullOrWhiteSpace(
                skillId))
        {
            return null;
        }

        _skills.TryGetValue(
            skillId.Trim(),
            out var skill);

        return skill;
    }
}
