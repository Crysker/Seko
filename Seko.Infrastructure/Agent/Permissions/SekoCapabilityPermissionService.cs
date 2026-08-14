using Seko.Infrastructure.Agent.Capabilities;
using Seko.Infrastructure.Agent.Tools;

namespace Seko.Infrastructure.Agent.Permissions;

public sealed class SekoCapabilityPermissionService
{
    private readonly SekoPermissionManager _permissionManager;
    private readonly SekoCapabilityRegistry _capabilityRegistry;
    private readonly SekoToolRegistry _toolRegistry;

    public SekoCapabilityPermissionService(
        SekoPermissionManager permissionManager,
        SekoCapabilityRegistry capabilityRegistry,
        SekoToolRegistry toolRegistry)
    {
        _permissionManager =
            permissionManager
            ?? throw new ArgumentNullException(
                nameof(permissionManager));

        _capabilityRegistry =
            capabilityRegistry
            ?? throw new ArgumentNullException(
                nameof(capabilityRegistry));

        _toolRegistry =
            toolRegistry
            ?? throw new ArgumentNullException(
                nameof(toolRegistry));
    }

    public async Task<CapabilityActivationState> SetDecisionAsync(
        string capabilityId,
        string permission,
        PermissionDecision decision,
        CancellationToken cancellationToken = default)
    {
        var capability =
            _capabilityRegistry.FindById(
                capabilityId)
            ?? throw new KeyNotFoundException(
                $"Capability '{capabilityId}' is not registered.");

        var source =
            _capabilityRegistry.GetSource(
                capabilityId)
            ?? throw new InvalidOperationException(
                $"Capability '{capabilityId}' does not have a registered source.");

        var normalizedPermission =
            permission?.Trim()
            ?? string.Empty;

        var requiredPermission =
            capability.Descriptor.RequiredPermissions.FirstOrDefault(
                required =>
                    required.Equals(
                        normalizedPermission,
                        StringComparison.OrdinalIgnoreCase));

        if (requiredPermission is null)
        {
            throw new InvalidOperationException(
                $"Capability '{capabilityId}' does not request permission '{permission}'.");
        }

        var previousPreference =
            _permissionManager.FindPreference(
                capability.Descriptor.Id,
                source,
                requiredPermission);

        await _permissionManager.SetDecisionAsync(
            capability.Descriptor.Id,
            source,
            requiredPermission,
            decision,
            cancellationToken);

        try
        {
            return
                _capabilityRegistry.Reevaluate(
                    capability.Descriptor.Id,
                    _permissionManager.Policy,
                    _toolRegistry);
        }
        catch
        {
            await _permissionManager.SetDecisionAsync(
                capability.Descriptor.Id,
                source,
                requiredPermission,
                previousPreference?.Decision
                    ?? PermissionDecision.Ask,
                CancellationToken.None);

            _capabilityRegistry.Reevaluate(
                capability.Descriptor.Id,
                _permissionManager.Policy,
                _toolRegistry);

            throw;
        }
    }

    public async Task<CapabilityActivationState> ClearDecisionsAsync(
        string capabilityId,
        CancellationToken cancellationToken = default)
    {
        var capability =
            _capabilityRegistry.FindById(
                capabilityId)
            ?? throw new KeyNotFoundException(
                $"Capability '{capabilityId}' is not registered.");

        var source =
            _capabilityRegistry.GetSource(
                capabilityId)
            ?? throw new InvalidOperationException(
                $"Capability '{capabilityId}' does not have a registered source.");

        await _permissionManager.ClearCapabilityAsync(
            capability.Descriptor.Id,
            source,
            cancellationToken);

        return
            _capabilityRegistry.Reevaluate(
                capability.Descriptor.Id,
                _permissionManager.Policy,
                _toolRegistry);
    }
}
