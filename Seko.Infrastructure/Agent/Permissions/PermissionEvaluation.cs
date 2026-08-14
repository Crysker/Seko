namespace Seko.Infrastructure.Agent.Permissions;

public sealed class PermissionEvaluation
{
    public IReadOnlyCollection<PermissionResult> Results
    {
        get;
    }

    public PermissionDecision OverallDecision
    {
        get;
    }

    public bool IsAllowed =>
        OverallDecision
        == PermissionDecision.Allow;

    public bool RequiresApproval =>
        OverallDecision
        == PermissionDecision.Ask;

    public bool IsDenied =>
        OverallDecision
        == PermissionDecision.Deny;

    public PermissionEvaluation(
        IEnumerable<PermissionResult> results)
    {
        ArgumentNullException.ThrowIfNull(
            results);

        var materialized =
            results.ToList();

        Results =
            materialized.AsReadOnly();

        OverallDecision =
            materialized.Any(
                result =>
                    result.Decision
                    == PermissionDecision.Deny)
                ? PermissionDecision.Deny
                : materialized.Any(
                    result =>
                        result.Decision
                        == PermissionDecision.Ask)
                    ? PermissionDecision.Ask
                    : PermissionDecision.Allow;
    }

    public PermissionDecision GetDecision(
        string permission)
    {
        if (string.IsNullOrWhiteSpace(
                permission))
        {
            throw new ArgumentException(
                "Permission cannot be empty.",
                nameof(permission));
        }

        var result =
            Results.FirstOrDefault(
                item =>
                    item.Permission.Equals(
                        permission.Trim(),
                        StringComparison.OrdinalIgnoreCase));

        if (result is null)
        {
            throw new KeyNotFoundException(
                $"Permission '{permission}' was not part of this evaluation.");
        }

        return
            result.Decision;
    }
}
