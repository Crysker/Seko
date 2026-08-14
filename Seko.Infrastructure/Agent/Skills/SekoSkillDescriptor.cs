namespace Seko.Infrastructure.Agent.Skills;

public sealed class SekoSkillDescriptor
{
    public string Id
    {
        get;
    }

    public string Name
    {
        get;
    }

    public string Description
    {
        get;
    }

    public IReadOnlyCollection<string> TriggerTerms
    {
        get;
    }

    public IReadOnlyCollection<string> RequiredAbilities
    {
        get;
    }

    public IReadOnlyCollection<string> PreferredAbilities
    {
        get;
    }

    public string Instructions
    {
        get;
    }

    public int Priority
    {
        get;
    }

    public SekoSkillDescriptor(
        string id,
        string name,
        string description,
        IEnumerable<string> triggerTerms,
        IEnumerable<string> requiredAbilities,
        IEnumerable<string> preferredAbilities,
        string instructions,
        int priority = 0)
    {
        Id =
            RequireValue(
                id,
                nameof(id));

        Name =
            RequireValue(
                name,
                nameof(name));

        Description =
            description?.Trim()
            ?? string.Empty;

        TriggerTerms =
            NormalizeValues(
                triggerTerms,
                nameof(triggerTerms));

        RequiredAbilities =
            NormalizeValues(
                requiredAbilities,
                nameof(requiredAbilities));

        PreferredAbilities =
            NormalizeValues(
                preferredAbilities,
                nameof(preferredAbilities));

        Instructions =
            instructions?.Trim()
            ?? string.Empty;

        Priority =
            priority;
    }

    private static string RequireValue(
        string value,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            throw new ArgumentException(
                "Value cannot be empty.",
                parameterName);
        }

        return
            value.Trim();
    }

    private static IReadOnlyCollection<string> NormalizeValues(
        IEnumerable<string> values,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(
            values);

        var normalized =
            values
                .Select(
                    value =>
                        value?.Trim()
                        ?? string.Empty)
                .ToList();

        if (normalized.Any(
                string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "Values cannot contain empty entries.",
                parameterName);
        }

        if (normalized.Count
            != normalized
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .Count())
        {
            throw new ArgumentException(
                "Values must be unique.",
                parameterName);
        }

        return
            normalized.AsReadOnly();
    }
}
