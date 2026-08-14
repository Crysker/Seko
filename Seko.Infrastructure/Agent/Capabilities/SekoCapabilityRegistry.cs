using Seko.Infrastructure.Agent.Permissions;
using Seko.Infrastructure.Agent.Tools;

namespace Seko.Infrastructure.Agent.Capabilities;

public sealed class SekoCapabilityRegistry
{
    private readonly Dictionary<string, ISekoCapability> _capabilities =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, CapabilityActivationState> _states =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, PermissionEvaluation> _permissionEvaluations =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, List<ISekoCapability>> _abilityProviders =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, List<ISekoCapability>> _knownAbilityProviders =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<ISekoCapability> Capabilities =>
        _capabilities.Values;

    public IReadOnlyCollection<ISekoCapability> ActiveCapabilities =>
        _capabilities
            .Where(
                pair =>
                    _states[pair.Key]
                    == CapabilityActivationState.Active)
            .Select(
                pair => pair.Value)
            .ToList()
            .AsReadOnly();

    public CapabilityActivationState Register(
        ISekoCapability capability,
        CapabilitySource source,
        SekoPermissionPolicy permissionPolicy,
        SekoToolRegistry toolRegistry)
    {
        ArgumentNullException.ThrowIfNull(
            capability);

        ArgumentNullException.ThrowIfNull(
            permissionPolicy);

        ArgumentNullException.ThrowIfNull(
            toolRegistry);

        var descriptor =
            capability.Descriptor
            ?? throw new InvalidOperationException(
                "Capability descriptor cannot be null.");

        if (_capabilities.ContainsKey(
                descriptor.Id))
        {
            throw new InvalidOperationException(
                $"Capability '{descriptor.Id}' is already registered.");
        }

        var tools =
            capability.Tools
            ?? throw new InvalidOperationException(
                $"Capability '{descriptor.Id}' returned a null tool collection.");

        var toolNames =
            tools
                .Select(
                    tool => tool.Name)
                .ToList();

        if (toolNames.Any(
                string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException(
                $"Capability '{descriptor.Id}' contains an empty tool name.");
        }

        if (toolNames.Count
            != toolNames
                .Distinct(
                    StringComparer.Ordinal)
                .Count())
        {
            throw new InvalidOperationException(
                $"Capability '{descriptor.Id}' contains duplicate tool names.");
        }

        foreach (var tool
                 in tools)
        {
            ArgumentNullException.ThrowIfNull(
                tool.Handler);
        }

        var permissionEvaluation =
            permissionPolicy.Evaluate(
                new PermissionRequest(
                    descriptor.Id,
                    source,
                    descriptor.RequiredPermissions));

        var state =
            permissionEvaluation.OverallDecision switch
            {
                PermissionDecision.Allow =>
                    CapabilityActivationState.Active,

                PermissionDecision.Ask =>
                    CapabilityActivationState.PendingApproval,

                PermissionDecision.Deny =>
                    CapabilityActivationState.Denied,

                _ =>
                    throw new ArgumentOutOfRangeException()
            };

        if (state
            == CapabilityActivationState.Active)
        {
            var registeredToolNames =
                toolRegistry.ToolNames
                    .ToHashSet(
                        StringComparer.Ordinal);

            var conflictingTool =
                toolNames.FirstOrDefault(
                    registeredToolNames.Contains);

            if (conflictingTool is not null)
            {
                throw new InvalidOperationException(
                    $"Tool '{conflictingTool}' is already registered by another active capability or tool provider.");
            }

            foreach (var tool
                     in tools)
            {
                toolRegistry.Register(
                    tool.Name,
                    tool.Handler);
            }
        }

        _capabilities.Add(
            descriptor.Id,
            capability);

        _states.Add(
            descriptor.Id,
            state);

        _permissionEvaluations.Add(
            descriptor.Id,
            permissionEvaluation);

        AddAbilityProvider(
            _knownAbilityProviders,
            capability);

        if (state
            == CapabilityActivationState.Active)
        {
            AddAbilityProvider(
                _abilityProviders,
                capability);
        }

        return state;
    }

    public bool Supports(
        string ability)
    {
        if (string.IsNullOrWhiteSpace(
                ability))
        {
            return false;
        }

        return
            _abilityProviders.ContainsKey(
                ability.Trim());
    }

    public bool KnowsAbility(
        string ability)
    {
        if (string.IsNullOrWhiteSpace(
                ability))
        {
            return false;
        }

        return
            _knownAbilityProviders.ContainsKey(
                ability.Trim());
    }

    public IReadOnlyCollection<ISekoCapability> GetProviders(
        string ability)
    {
        return
            GetProvidersFrom(
                _abilityProviders,
                ability);
    }

    public IReadOnlyCollection<ISekoCapability> GetKnownProviders(
        string ability)
    {
        return
            GetProvidersFrom(
                _knownAbilityProviders,
                ability);
    }

    public ISekoCapability? FindById(
        string capabilityId)
    {
        if (string.IsNullOrWhiteSpace(
                capabilityId))
        {
            return null;
        }

        _capabilities.TryGetValue(
            capabilityId.Trim(),
            out var capability);

        return capability;
    }

    public CapabilityActivationState? GetState(
        string capabilityId)
    {
        if (string.IsNullOrWhiteSpace(
                capabilityId))
        {
            return null;
        }

        return
            _states.TryGetValue(
                capabilityId.Trim(),
                out var state)
                ? state
                : null;
    }

    public PermissionEvaluation? GetPermissionEvaluation(
        string capabilityId)
    {
        if (string.IsNullOrWhiteSpace(
                capabilityId))
        {
            return null;
        }

        _permissionEvaluations.TryGetValue(
            capabilityId.Trim(),
            out var evaluation);

        return evaluation;
    }

    private static void AddAbilityProvider(
        IDictionary<string, List<ISekoCapability>> providersByAbility,
        ISekoCapability capability)
    {
        foreach (var ability
                 in capability.Descriptor.Abilities)
        {
            if (!providersByAbility.TryGetValue(
                    ability,
                    out var providers))
            {
                providers =
                    new List<ISekoCapability>();

                providersByAbility.Add(
                    ability,
                    providers);
            }

            providers.Add(
                capability);
        }
    }

    private static IReadOnlyCollection<ISekoCapability> GetProvidersFrom(
        IReadOnlyDictionary<string, List<ISekoCapability>> providersByAbility,
        string ability)
    {
        if (string.IsNullOrWhiteSpace(
                ability))
        {
            return
                Array.Empty<ISekoCapability>();
        }

        if (!providersByAbility.TryGetValue(
                ability.Trim(),
                out var providers))
        {
            return
                Array.Empty<ISekoCapability>();
        }

        return
            providers.AsReadOnly();
    }
}
