namespace Seko.Infrastructure.Agent.Permissions;

public sealed record PermissionPreference(
    string CapabilityId,
    CapabilitySource Source,
    string Permission,
    PermissionDecision Decision);
