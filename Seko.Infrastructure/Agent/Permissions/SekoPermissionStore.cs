using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Seko.Infrastructure.Agent.Permissions;

public sealed class SekoPermissionStore
{
    private static readonly JsonSerializerOptions JsonOptions =
        CreateJsonOptions();

    public string FilePath
    {
        get;
    }

    public SekoPermissionStore(
        string? filePath = null)
    {
        FilePath =
            Path.GetFullPath(
                string.IsNullOrWhiteSpace(
                    filePath)
                    ? GetDefaultFilePath()
                    : filePath);
    }

    public IReadOnlyCollection<PermissionPreference> Load()
    {
        if (!File.Exists(
                FilePath))
        {
            return
                Array.Empty<PermissionPreference>();
        }

        if (new FileInfo(
                FilePath).Length > 512_000)
        {
            throw new InvalidDataException(
                "Permission store is too large.");
        }

        var json =
            File.ReadAllText(
                FilePath);

        var document =
            JsonSerializer.Deserialize<PermissionStoreDocument>(
                json,
                JsonOptions)
            ?? throw new InvalidDataException(
                "Permission store is empty or invalid.");

        if (document.Version != 1)
        {
            throw new InvalidDataException(
                $"Unsupported permission store version '{document.Version}'.");
        }

        var decisions =
            document.Decisions
            ?? new List<PermissionPreference>();

        Validate(
            decisions);

        return
            decisions
                .ToList()
                .AsReadOnly();
    }

    public async Task SaveAsync(
        IEnumerable<PermissionPreference> preferences,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            preferences);

        var materialized =
            preferences.ToList();

        Validate(
            materialized);

        var directory =
            Path.GetDirectoryName(
                FilePath)
            ?? throw new InvalidOperationException(
                "Permission store path does not have a parent directory.");

        Directory.CreateDirectory(
            directory);

        var document =
            new PermissionStoreDocument
            {
                Version =
                    1,

                Decisions =
                    materialized
                        .OrderBy(
                            preference =>
                                preference.CapabilityId,
                            StringComparer.OrdinalIgnoreCase)
                        .ThenBy(
                            preference =>
                                preference.Permission,
                            StringComparer.OrdinalIgnoreCase)
                        .ToList()
            };

        var json =
            JsonSerializer.Serialize(
                document,
                JsonOptions);

        var temporaryPath =
            FilePath
            + ".tmp."
            + Guid.NewGuid().ToString(
                "N");

        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                json,
                new UTF8Encoding(false),
                cancellationToken);

            File.Move(
                temporaryPath,
                FilePath,
                true);
        }
        finally
        {
            if (File.Exists(
                    temporaryPath))
            {
                try
                {
                    File.Delete(
                        temporaryPath);
                }
                catch
                {
                    // Best effort cleanup. The destination remains authoritative.
                }
            }
        }
    }

    private static void Validate(
        IReadOnlyCollection<PermissionPreference> preferences)
    {
        if (preferences.Count > 1_024)
        {
            throw new InvalidDataException(
                "Permission store contains too many decisions.");
        }

        foreach (var preference
                 in preferences)
        {
            if (string.IsNullOrWhiteSpace(
                    preference.CapabilityId))
            {
                throw new InvalidDataException(
                    "Persisted capability id cannot be empty.");
            }

            if (string.IsNullOrWhiteSpace(
                    preference.Permission))
            {
                throw new InvalidDataException(
                    "Persisted permission cannot be empty.");
            }

            if (preference.Permission.Contains(
                    '*'))
            {
                throw new InvalidDataException(
                    "Persisted user permission decisions must use exact permission names.");
            }

            if (!Enum.IsDefined(
                    typeof(CapabilitySource),
                    preference.Source))
            {
                throw new InvalidDataException(
                    "Persisted capability source is invalid.");
            }

            if (!Enum.IsDefined(
                    typeof(PermissionDecision),
                    preference.Decision))
            {
                throw new InvalidDataException(
                    "Persisted permission decision is invalid.");
            }

            if (preference.Decision
                == PermissionDecision.Ask)
            {
                throw new InvalidDataException(
                    "Ask decisions are represented by the absence of a persisted override.");
            }
        }

        var duplicate =
            preferences
                .GroupBy(
                    preference =>
                        (
                            CapabilityId:
                                preference.CapabilityId.Trim(),
                            preference.Source,
                            Permission:
                                preference.Permission.Trim()
                        ),
                    PermissionPreferenceKeyComparer.Instance)
                .FirstOrDefault(
                    group =>
                        group.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidDataException(
                "Permission store contains duplicate decisions for one capability permission.");
        }
    }

    private static string GetDefaultFilePath()
    {
        return
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "Seko",
                "Config",
                "permissions.json");
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options =
            new JsonSerializerOptions
            {
                WriteIndented =
                    true,

                PropertyNameCaseInsensitive =
                    true
            };

        options.Converters.Add(
            new JsonStringEnumConverter());

        return options;
    }

    private sealed class PermissionStoreDocument
    {
        public int Version
        {
            get;
            set;
        }

        public List<PermissionPreference>? Decisions
        {
            get;
            set;
        }
    }

    private sealed class PermissionPreferenceKeyComparer :
        IEqualityComparer<(string CapabilityId, CapabilitySource Source, string Permission)>
    {
        public static PermissionPreferenceKeyComparer Instance
        {
            get;
        } =
            new();

        public bool Equals(
            (string CapabilityId, CapabilitySource Source, string Permission) x,
            (string CapabilityId, CapabilitySource Source, string Permission) y)
        {
            return
                x.Source
                == y.Source
                && x.CapabilityId.Equals(
                    y.CapabilityId,
                    StringComparison.OrdinalIgnoreCase)
                && x.Permission.Equals(
                    y.Permission,
                    StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(
            (string CapabilityId, CapabilitySource Source, string Permission) obj)
        {
            return
                HashCode.Combine(
                    StringComparer.OrdinalIgnoreCase.GetHashCode(
                        obj.CapabilityId),
                    obj.Source,
                    StringComparer.OrdinalIgnoreCase.GetHashCode(
                        obj.Permission));
        }
    }
}
