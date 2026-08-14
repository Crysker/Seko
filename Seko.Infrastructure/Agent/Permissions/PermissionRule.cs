namespace Seko.Infrastructure.Agent.Permissions;

public sealed class PermissionRule
{
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
    {
        if (string.IsNullOrWhiteSpace(
                pattern))
        {
            throw new ArgumentException(
                "Permission pattern cannot be empty.",
                nameof(pattern));
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
