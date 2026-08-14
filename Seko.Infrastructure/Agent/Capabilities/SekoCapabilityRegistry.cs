using Seko.Infrastructure.Agent.Tools;

namespace Seko.Infrastructure.Agent.Capabilities;

public sealed class SekoCapabilityRegistry
{
    private readonly Dictionary<string, ISekoCapability> _capabilities =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, List<ISekoCapability>> _abilityProviders =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<ISekoCapability> Capabilities =>
        _capabilities.Values;

    public void Register(
        ISekoCapability capability,
        SekoToolRegistry toolRegistry)
    {
        ArgumentNullException.ThrowIfNull(
            capability);

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
                $"Tool '{conflictingTool}' is already registered by another capability or tool provider.");
        }

        foreach (var tool
                 in tools)
        {
            ArgumentNullException.ThrowIfNull(
                tool.Handler);
        }

        foreach (var tool
                 in tools)
        {
            toolRegistry.Register(
                tool.Name,
                tool.Handler);
        }

        _capabilities.Add(
            descriptor.Id,
            capability);

        foreach (var ability
                 in descriptor.Abilities)
        {
            if (!_abilityProviders.TryGetValue(
                    ability,
                    out var providers))
            {
                providers =
                    new List<ISekoCapability>();

                _abilityProviders.Add(
                    ability,
                    providers);
            }

            providers.Add(
                capability);
        }
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

    public IReadOnlyCollection<ISekoCapability> GetProviders(
        string ability)
    {
        if (string.IsNullOrWhiteSpace(
                ability))
        {
            return
                Array.Empty<ISekoCapability>();
        }

        if (!_abilityProviders.TryGetValue(
                ability.Trim(),
                out var providers))
        {
            return
                Array.Empty<ISekoCapability>();
        }

        return
            providers.AsReadOnly();
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
}
