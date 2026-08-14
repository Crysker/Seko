namespace Seko.Infrastructure.Agent.Permissions;

public sealed class PermissionRequest
{
    public string CapabilityId
    {
        get;
    }

    public CapabilitySource Source
    {
        get;
    }

    public IReadOnlyCollection<string> Permissions
    {
        get;
    }

    public PermissionRequest(
        string capabilityId,
        CapabilitySource source,
        IEnumerable<string> permissions)
    {
        if (string.IsNullOrWhiteSpace(
                capabilityId))
        {
            throw new ArgumentException(
                "Capability id cannot be empty.",
                nameof(capabilityId));
        }

        ArgumentNullException.ThrowIfNull(
            permissions);

        var normalized =
            permissions
                .Select(
                    permission =>
                        permission?.Trim()
                        ?? string.Empty)
                .ToList();

        if (normalized.Any(
                string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "Permission values cannot be empty.",
                nameof(permissions));
        }

        if (normalized.Count
            != normalized
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .Count())
        {
            throw new ArgumentException(
                "Permission values must be unique.",
                nameof(permissions));
        }

        CapabilityId =
            capabilityId.Trim();

        Source =
            source;

        Permissions =
            normalized.AsReadOnly();
    }
}
