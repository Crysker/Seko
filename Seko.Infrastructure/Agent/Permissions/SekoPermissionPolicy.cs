namespace Seko.Infrastructure.Agent.Permissions;

public sealed class SekoPermissionPolicy
{
    private static readonly HashSet<string> ProtectedPermissions =
        new(
            new[]
            {
                "self.modify.kernel",
                "permissions.modify"
            },
            StringComparer.OrdinalIgnoreCase);

    private readonly IReadOnlyList<PermissionRule> _rules;

    public PermissionDecision DefaultDecision
    {
        get;
    }

    public SekoPermissionPolicy(
        IEnumerable<PermissionRule>? rules = null,
        PermissionDecision defaultDecision = PermissionDecision.Ask)
    {
        if (!Enum.IsDefined(
                typeof(PermissionDecision),
                defaultDecision))
        {
            throw new ArgumentOutOfRangeException(
                nameof(defaultDecision));
        }

        _rules =
            (rules
             ?? Array.Empty<PermissionRule>())
                .ToList()
                .AsReadOnly();

        DefaultDecision =
            defaultDecision;
    }

    public static bool IsProtectedPermission(
        string permission)
    {
        return
            !string.IsNullOrWhiteSpace(
                permission)
            && ProtectedPermissions.Contains(
                permission.Trim());
    }

    public static SekoPermissionPolicy CreateDefault(
        IEnumerable<PermissionRule>? additionalRules = null)
    {
        var rules =
            new List<PermissionRule>
            {
                new(
                    CapabilitySource.BuiltIn,
                    "*",
                    PermissionDecision.Allow)
            };

        if (additionalRules is not null)
        {
            rules.AddRange(
                additionalRules);
        }

        return
            new SekoPermissionPolicy(
                rules,
                PermissionDecision.Ask);
    }

    public PermissionDecision Evaluate(
        CapabilitySource source,
        string permission)
    {
        return
            Evaluate(
                null,
                source,
                permission);
    }

    public PermissionDecision Evaluate(
        string? capabilityId,
        CapabilitySource source,
        string permission)
    {
        if (string.IsNullOrWhiteSpace(
                permission))
        {
            throw new ArgumentException(
                "Permission cannot be empty.",
                nameof(permission));
        }

        var normalized =
            permission.Trim();

        if (IsProtectedPermission(
                normalized))
        {
            return
                PermissionDecision.Deny;
        }

        PermissionRule? bestRule =
            null;

        var bestIndex =
            -1;

        for (var index = 0;
             index < _rules.Count;
             index++)
        {
            var rule =
                _rules[index];

            if (!rule.Matches(
                    capabilityId,
                    source,
                    normalized))
            {
                continue;
            }

            if (bestRule is null
                || IsMoreSpecific(
                    rule,
                    index,
                    bestRule,
                    bestIndex))
            {
                bestRule =
                    rule;

                bestIndex =
                    index;
            }
        }

        return
            bestRule?.Decision
            ?? DefaultDecision;
    }

    public PermissionEvaluation Evaluate(
        PermissionRequest request)
    {
        ArgumentNullException.ThrowIfNull(
            request);

        return
            new PermissionEvaluation(
                request.Permissions.Select(
                    permission =>
                        new PermissionResult(
                            permission,
                            Evaluate(
                                request.CapabilityId,
                                request.Source,
                                permission))));
    }

    private static bool IsMoreSpecific(
        PermissionRule candidate,
        int candidateIndex,
        PermissionRule current,
        int currentIndex)
    {
        if (candidate.IsExact
            != current.IsExact)
        {
            return
                candidate.IsExact;
        }

        if (candidate.PrefixLength
            != current.PrefixLength)
        {
            return
                candidate.PrefixLength
                > current.PrefixLength;
        }

        if ((candidate.CapabilityId is not null)
            != (current.CapabilityId is not null))
        {
            return
                candidate.CapabilityId is not null;
        }

        if (candidate.Source.HasValue
            != current.Source.HasValue)
        {
            return
                candidate.Source.HasValue;
        }

        return
            candidateIndex
            > currentIndex;
    }
}
