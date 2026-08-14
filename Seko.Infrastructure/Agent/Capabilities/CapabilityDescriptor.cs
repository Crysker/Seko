namespace Seko.Infrastructure.Agent.Capabilities;

public sealed class CapabilityDescriptor
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

    public IReadOnlyCollection<string> Abilities
    {
        get;
    }

    public IReadOnlyCollection<string> RequiredPermissions
    {
        get;
    }

    public CapabilityDescriptor(
        string id,
        string name,
        string description,
        IEnumerable<string> abilities,
        IEnumerable<string>? requiredPermissions = null)
    {
        if (string.IsNullOrWhiteSpace(
                id))
        {
            throw new ArgumentException(
                "Capability id cannot be empty.",
                nameof(id));
        }

        if (string.IsNullOrWhiteSpace(
                name))
        {
            throw new ArgumentException(
                "Capability name cannot be empty.",
                nameof(name));
        }

        ArgumentNullException.ThrowIfNull(
            abilities);

        Id =
            id.Trim();

        Name =
            name.Trim();

        Description =
            description?.Trim()
            ?? string.Empty;

        Abilities =
            NormalizeValues(
                abilities,
                "ability");

        RequiredPermissions =
            NormalizeValues(
                requiredPermissions
                ?? Array.Empty<string>(),
                "permission");
    }

    private static IReadOnlyCollection<string> NormalizeValues(
        IEnumerable<string> values,
        string valueKind)
    {
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
                $"Capability {valueKind} values cannot be empty.");
        }

        if (normalized.Count
            != normalized
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .Count())
        {
            throw new ArgumentException(
                $"Capability {valueKind} values must be unique.");
        }

        return
            normalized.AsReadOnly();
    }
}
