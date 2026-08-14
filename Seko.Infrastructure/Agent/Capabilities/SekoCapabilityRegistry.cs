using Seko.Infrastructure.Agent.Permissions;
using Seko.Infrastructure.Agent.Tools;

namespace Seko.Infrastructure.Agent.Capabilities;

public sealed class SekoCapabilityRegistry
{
    private readonly Dictionary<string, ISekoCapability> _capabilities =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, CapabilitySource> _sources =
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

    public IReadOnlyCollection<string> ActiveAbilities =>
        _abilityProviders.Keys
            .OrderBy(
                value => value,
                StringComparer.OrdinalIgnoreCase)
            .ToList()
            .AsReadOnly();

    public IReadOnlyCollection<string> KnownAbilities =>
        _knownAbilityProviders.Keys
            .OrderBy(
                value => value,
                StringComparer.OrdinalIgnoreCase)
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
            ValidateCapability(
                capability);

        if (_capabilities.ContainsKey(
                descriptor.Id))
        {
            throw new InvalidOperationException(
                $"Capability '{descriptor.Id}' is already registered.");
        }

        var permissionEvaluation =
            EvaluatePermissions(
                capability,
                source,
                permissionPolicy);

        var state =
            ToActivationState(
                permissionEvaluation);

        if (state
            == CapabilityActivationState.Active)
        {
            EnsureToolsCanActivate(
                capability,
                toolRegistry);

            RegisterTools(
                capability,
                toolRegistry);
        }

        _capabilities.Add(
            descriptor.Id,
            capability);

        _sources.Add(
            descriptor.Id,
            source);

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

    public CapabilityActivationState Reevaluate(
        string capabilityId,
        SekoPermissionPolicy permissionPolicy,
        SekoToolRegistry toolRegistry)
    {
        ArgumentNullException.ThrowIfNull(
            permissionPolicy);

        ArgumentNullException.ThrowIfNull(
            toolRegistry);

        var capability =
            FindById(
                capabilityId)
            ?? throw new KeyNotFoundException(
                $"Capability '{capabilityId}' is not registered.");

        var source =
            GetSource(
                capabilityId)
            ?? throw new InvalidOperationException(
                $"Capability '{capabilityId}' does not have a registered source.");

        var previousState =
            _states[capability.Descriptor.Id];

        var permissionEvaluation =
            EvaluatePermissions(
                capability,
                source,
                permissionPolicy);

        var nextState =
            ToActivationState(
                permissionEvaluation);

        if (previousState
            != CapabilityActivationState.Active
            && nextState
            == CapabilityActivationState.Active)
        {
            EnsureToolsCanActivate(
                capability,
                toolRegistry);

            RegisterTools(
                capability,
                toolRegistry);

            AddAbilityProvider(
                _abilityProviders,
                capability);
        }
        else if (previousState
                 == CapabilityActivationState.Active
                 && nextState
                 != CapabilityActivationState.Active)
        {
            UnregisterTools(
                capability,
                toolRegistry);

            RemoveAbilityProvider(
                _abilityProviders,
                capability);
        }

        _states[capability.Descriptor.Id] =
            nextState;

        _permissionEvaluations[capability.Descriptor.Id] =
            permissionEvaluation;

        return nextState;
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

    public CapabilitySource? GetSource(
        string capabilityId)
    {
        if (string.IsNullOrWhiteSpace(
                capabilityId))
        {
            return null;
        }

        return
            _sources.TryGetValue(
                capabilityId.Trim(),
                out var source)
                ? source
                : null;
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

    private static CapabilityDescriptor ValidateCapability(
        ISekoCapability capability)
    {
        var descriptor =
            capability.Descriptor
            ?? throw new InvalidOperationException(
                "Capability descriptor cannot be null.");

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

        return descriptor;
    }

    private static PermissionEvaluation EvaluatePermissions(
        ISekoCapability capability,
        CapabilitySource source,
        SekoPermissionPolicy permissionPolicy)
    {
        return
            permissionPolicy.Evaluate(
                new PermissionRequest(
                    capability.Descriptor.Id,
                    source,
                    capability.Descriptor.RequiredPermissions));
    }

    private static CapabilityActivationState ToActivationState(
        PermissionEvaluation permissionEvaluation)
    {
        return
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
    }

    private static void EnsureToolsCanActivate(
        ISekoCapability capability,
        SekoToolRegistry toolRegistry)
    {
        var registeredToolNames =
            toolRegistry.ToolNames
                .ToHashSet(
                    StringComparer.Ordinal);

        var conflictingTool =
            capability.Tools
                .Select(
                    tool => tool.Name)
                .FirstOrDefault(
                    registeredToolNames.Contains);

        if (conflictingTool is not null)
        {
            throw new InvalidOperationException(
                $"Tool '{conflictingTool}' is already registered by another active capability or tool provider.");
        }
    }

    private static void RegisterTools(
        ISekoCapability capability,
        SekoToolRegistry toolRegistry)
    {
        foreach (var tool
                 in capability.Tools)
        {
            toolRegistry.Register(
                tool.Name,
                tool.Handler);
        }
    }

    private static void UnregisterTools(
        ISekoCapability capability,
        SekoToolRegistry toolRegistry)
    {
        foreach (var tool
                 in capability.Tools)
        {
            toolRegistry.Unregister(
                tool.Name);
        }
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

            if (!providers.Contains(
                    capability))
            {
                providers.Add(
                    capability);
            }
        }
    }

    private static void RemoveAbilityProvider(
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
                continue;
            }

            providers.Remove(
                capability);

            if (providers.Count == 0)
            {
                providersByAbility.Remove(
                    ability);
            }
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
