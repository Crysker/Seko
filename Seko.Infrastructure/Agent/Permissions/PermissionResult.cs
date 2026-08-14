namespace Seko.Infrastructure.Agent.Permissions;

public sealed record PermissionResult(
    string Permission,
    PermissionDecision Decision);
