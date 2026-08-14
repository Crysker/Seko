using System.Text.Json;

namespace Seko.Infrastructure.Agent.Permissions;

public sealed class SekoPermissionManager
{
    private readonly SekoPermissionStore _store;

    private readonly List<PermissionPreference> _preferences;

    public SekoPermissionPolicy Policy
    {
        get;
        private set;
    }

    public string? LoadWarning
    {
        get;
    }

    public IReadOnlyCollection<PermissionPreference> Preferences =>
        _preferences.AsReadOnly();

    private SekoPermissionManager(
        SekoPermissionStore store,
        IEnumerable<PermissionPreference> preferences,
        string? loadWarning)
    {
        _store =
            store;

        _preferences =
            preferences.ToList();

        LoadWarning =
            loadWarning;

        Policy =
            BuildPolicy(
                _preferences);
    }

    public static SekoPermissionManager CreateDefault()
    {
        return
            Load(
                new SekoPermissionStore());
    }

    public static SekoPermissionManager Load(
        SekoPermissionStore store)
    {
        ArgumentNullException.ThrowIfNull(
            store);

        try
        {
            return
                new SekoPermissionManager(
                    store,
                    store.Load(),
                    null);
        }
        catch (Exception exception)
            when (exception
                  is IOException
                  or UnauthorizedAccessException
                  or JsonException
                  or InvalidDataException)
        {
            return
                new SekoPermissionManager(
                    store,
                    Array.Empty<PermissionPreference>(),
                    "Persisted permission decisions could not be loaded. "
                    + "Seko failed closed and will ask again. "
                    + exception.Message);
        }
    }

    public PermissionPreference? FindPreference(
        string capabilityId,
        CapabilitySource source,
        string permission)
    {
        ValidateKey(
            capabilityId,
            permission);

        return
            _preferences.FirstOrDefault(
                preference =>
                    preference.Source
                    == source
                    && preference.CapabilityId.Equals(
                        capabilityId.Trim(),
                        StringComparison.OrdinalIgnoreCase)
                    && preference.Permission.Equals(
                        permission.Trim(),
                        StringComparison.OrdinalIgnoreCase));
    }

    public async Task SetDecisionAsync(
        string capabilityId,
        CapabilitySource source,
        string permission,
        PermissionDecision decision,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(
            capabilityId,
            permission);

        var normalizedCapabilityId =
            capabilityId.Trim();

        var normalizedPermission =
            permission.Trim();

        if (decision
            == PermissionDecision.Allow
            && SekoPermissionPolicy.IsProtectedPermission(
                normalizedPermission))
        {
            throw new InvalidOperationException(
                $"Permission '{normalizedPermission}' is protected by the Seko kernel and cannot be allowed by a persisted override.");
        }

        var existing =
            _preferences.FindIndex(
                preference =>
                    preference.Source
                    == source
                    && preference.CapabilityId.Equals(
                        normalizedCapabilityId,
                        StringComparison.OrdinalIgnoreCase)
                    && preference.Permission.Equals(
                        normalizedPermission,
                        StringComparison.OrdinalIgnoreCase));

        var previous =
            _preferences.ToList();

        if (existing >= 0)
        {
            _preferences.RemoveAt(
                existing);
        }

        if (decision
            != PermissionDecision.Ask)
        {
            _preferences.Add(
                new PermissionPreference(
                    normalizedCapabilityId,
                    source,
                    normalizedPermission,
                    decision));
        }

        try
        {
            await _store.SaveAsync(
                _preferences,
                cancellationToken);
        }
        catch
        {
            _preferences.Clear();

            _preferences.AddRange(
                previous);

            throw;
        }

        Policy =
            BuildPolicy(
                _preferences);
    }

    public async Task ClearCapabilityAsync(
        string capabilityId,
        CapabilitySource source,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(
                capabilityId))
        {
            throw new ArgumentException(
                "Capability id cannot be empty.",
                nameof(capabilityId));
        }

        var normalized =
            capabilityId.Trim();

        var previous =
            _preferences.ToList();

        _preferences.RemoveAll(
            preference =>
                preference.Source
                == source
                && preference.CapabilityId.Equals(
                    normalized,
                    StringComparison.OrdinalIgnoreCase));

        try
        {
            await _store.SaveAsync(
                _preferences,
                cancellationToken);
        }
        catch
        {
            _preferences.Clear();

            _preferences.AddRange(
                previous);

            throw;
        }

        Policy =
            BuildPolicy(
                _preferences);
    }

    private static SekoPermissionPolicy BuildPolicy(
        IEnumerable<PermissionPreference> preferences)
    {
        var rules =
            preferences.Select(
                preference =>
                    new PermissionRule(
                        preference.CapabilityId,
                        preference.Source,
                        preference.Permission,
                        preference.Decision));

        return
            SekoPermissionPolicy.CreateDefault(
                rules);
    }

    private static void ValidateKey(
        string capabilityId,
        string permission)
    {
        if (string.IsNullOrWhiteSpace(
                capabilityId))
        {
            throw new ArgumentException(
                "Capability id cannot be empty.",
                nameof(capabilityId));
        }

        if (string.IsNullOrWhiteSpace(
                permission))
        {
            throw new ArgumentException(
                "Permission cannot be empty.",
                nameof(permission));
        }

        if (permission.Contains(
                '*'))
        {
            throw new ArgumentException(
                "Persisted permission decisions must use exact permission names.",
                nameof(permission));
        }
    }
}
