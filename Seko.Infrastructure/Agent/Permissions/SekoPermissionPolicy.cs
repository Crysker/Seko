namespace Seko.Infrastructure.Agent.Permissions;

public sealed class SekoPermissionPolicy
{
    private readonly IReadOnlyList<PermissionRule> _rules;

    public PermissionDecision DefaultDecision
    {
        get;
    }

    public SekoPermissionPolicy(
        IEnumerable<PermissionRule>? rules = null,
        PermissionDecision defaultDecision = PermissionDecision.Ask)
    {
        _rules =
            (rules
             ?? Array.Empty<PermissionRule>())
                .ToList()
                .AsReadOnly();

        DefaultDecision =
            defaultDecision;
    }

    public static SekoPermissionPolicy CreateDefault()
    {
        return
            new SekoPermissionPolicy(
                new[]
                {
                    new PermissionRule(
                        null,
                        "self.modify.kernel",
                        PermissionDecision.Deny),

                    new PermissionRule(
                        null,
                        "permissions.modify",
                        PermissionDecision.Deny),

                    new PermissionRule(
                        CapabilitySource.BuiltIn,
                        "*",
                        PermissionDecision.Allow)
                },
                PermissionDecision.Ask);
    }

    public PermissionDecision Evaluate(
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
