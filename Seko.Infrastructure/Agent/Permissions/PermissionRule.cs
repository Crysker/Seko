namespace Seko.Infrastructure.Agent.Permissions;

public sealed class PermissionRule
{
    public string? CapabilityId
    {
        get;
    }

    public CapabilitySource? Source
    {
        get;
    }

    public string Pattern
    {
        get;
    }

    public PermissionDecision Decision
    {
        get;
    }

    public bool IsExact =>
        !Pattern.EndsWith(
            "*",
            StringComparison.Ordinal);

    public int PrefixLength =>
        IsExact
            ? Pattern.Length
            : Pattern.Length - 1;

    public PermissionRule(
        CapabilitySource? source,
        string pattern,
        PermissionDecision decision)
        : this(
            null,
            source,
            pattern,
            decision)
    {
    }

    public PermissionRule(
        string? capabilityId,
        CapabilitySource? source,
        string pattern,
        PermissionDecision decision)
    {
        if (string.IsNullOrWhiteSpace(
                pattern))
        {
            throw new ArgumentException(
                "Permission pattern cannot be empty.",
                nameof(pattern));
        }

        if (!Enum.IsDefined(
                typeof(PermissionDecision),
                decision))
        {
            throw new ArgumentOutOfRangeException(
                nameof(decision));
        }

        if (source.HasValue
            && !Enum.IsDefined(
                typeof(CapabilitySource),
                source.Value))
        {
            throw new ArgumentOutOfRangeException(
                nameof(source));
        }

        var normalized =
            pattern.Trim();

        var firstWildcard =
            normalized.IndexOf(
                '*');

        if (firstWildcard >= 0
            && (firstWildcard != normalized.Length - 1
                || normalized.LastIndexOf(
                    '*')
                    != firstWildcard))
        {
            throw new ArgumentException(
                "Permission patterns may contain at most one wildcard and it must be the final character.",
                nameof(pattern));
        }

        CapabilityId =
            string.IsNullOrWhiteSpace(
                capabilityId)
                ? null
                : capabilityId.Trim();

        Source =
            source;

        Pattern =
            normalized;

        Decision =
            decision;
    }

    public bool Matches(
        CapabilitySource source,
        string permission)
    {
        return
            Matches(
                null,
                source,
                permission);
    }

    public bool Matches(
        string? capabilityId,
        CapabilitySource source,
        string permission)
    {
        if (CapabilityId is not null
            && !CapabilityId.Equals(
                capabilityId,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (Source.HasValue
            && Source.Value != source)
        {
            return false;
        }

        if (IsExact)
        {
            return
                Pattern.Equals(
                    permission,
                    StringComparison.OrdinalIgnoreCase);
        }

        var prefix =
            Pattern[..^1];

        return
            permission.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase);
    }
}
