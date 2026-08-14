using System.Text;
using System.Text.Json;

namespace Seko.Infrastructure.Agent.Extensions;

public sealed class SekoExtensionInstaller
{
    private static readonly HashSet<string> AllowedTextExtensions =
        new(
            new[]
            {
                ".json",
                ".md",
                ".txt",
                ".yaml",
                ".yml",
                ".toml"
            },
            StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            WriteIndented =
                true,

            PropertyNameCaseInsensitive =
                true
        };

    private readonly SekoExtensionValidator _validator =
        new();

    public string RootPath
    {
        get;
    }

    public string StagingRoot
    {
        get;
    }

    public string InstalledRoot
    {
        get;
    }

    public string BackupRoot
    {
        get;
    }

    public SekoExtensionInstaller(
        string? rootPath = null)
    {
        RootPath =
            Path.GetFullPath(
                string.IsNullOrWhiteSpace(
                    rootPath)
                    ? Path.Combine(
                        Environment.GetFolderPath(
                            Environment.SpecialFolder.LocalApplicationData),
                        "Seko",
                        "Extensions")
                    : rootPath);

        StagingRoot =
            Path.Combine(
                RootPath,
                "Staging");

        InstalledRoot =
            Path.Combine(
                RootPath,
                "Installed");

        BackupRoot =
            Path.Combine(
                RootPath,
                "Backups");
    }

    public async Task<ExtensionCandidate> PrepareCandidateAsync(
        SekoExtensionManifest manifest,
        IReadOnlyDictionary<string, string>? textFiles = null,
        CancellationToken cancellationToken = default)
    {
        var errors =
            _validator.Validate(
                manifest);

        if (errors.Count > 0)
        {
            throw new InvalidDataException(
                string.Join(
                    " ",
                    errors));
        }

        var files =
            textFiles
            ?? new Dictionary<string, string>();

        if (files.Count > 32)
        {
            throw new InvalidDataException(
                "A declarative extension candidate may contain at most 32 additional text files.");
        }

        EnsureControlledDirectory(
            RootPath);

        EnsureControlledDirectory(
            StagingRoot);

        var candidateRoot =
            Path.Combine(
                StagingRoot,
                manifest.Id.Trim()
                + "-"
                + Guid.NewGuid().ToString(
                    "N"));

        Directory.CreateDirectory(
            candidateRoot);

        try
        {
            var manifestJson =
                JsonSerializer.Serialize(
                    manifest,
                    JsonOptions);

            await File.WriteAllTextAsync(
                Path.Combine(
                    candidateRoot,
                    "extension.json"),
                manifestJson,
                new UTF8Encoding(false),
                cancellationToken);

            foreach (var file
                     in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var relativePath =
                    ValidateRelativeTextPath(
                        file.Key);

                if (file.Value.Length > 256_000)
                {
                    throw new InvalidDataException(
                        $"Extension text file '{relativePath}' is too large.");
                }

                var destination =
                    ResolveInside(
                        candidateRoot,
                        relativePath);

                Directory.CreateDirectory(
                    Path.GetDirectoryName(
                        destination)!);

                await File.WriteAllTextAsync(
                    destination,
                    file.Value,
                    new UTF8Encoding(false),
                    cancellationToken);
            }

            ValidateCandidateDirectory(
                candidateRoot);

            return
                new ExtensionCandidate(
                    candidateRoot,
                    manifest);
        }
        catch
        {
            TryDeleteDirectory(
                candidateRoot);

            throw;
        }
    }

    public ExtensionCandidate ValidateCandidate(
        string candidateRoot)
    {
        var root =
            Path.GetFullPath(
                candidateRoot);

        EnsureInside(
            StagingRoot,
            root);

        ValidateCandidateDirectory(
            root);

        var manifest =
            JsonSerializer.Deserialize<SekoExtensionManifest>(
                File.ReadAllText(
                    Path.Combine(
                        root,
                        "extension.json")),
                JsonOptions)
            ?? throw new InvalidDataException(
                "Extension manifest is empty.");

        var errors =
            _validator.Validate(
                manifest);

        if (errors.Count > 0)
        {
            throw new InvalidDataException(
                string.Join(
                    " ",
                    errors));
        }

        return
            new ExtensionCandidate(
                root,
                manifest);
    }

    public ExtensionInstallResult Install(
        string candidateRoot)
    {
        var candidate =
            ValidateCandidate(
                candidateRoot);

        EnsureControlledDirectory(
            RootPath);

        EnsureControlledDirectory(
            InstalledRoot);

        EnsureControlledDirectory(
            BackupRoot);

        var destination =
            Path.Combine(
                InstalledRoot,
                candidate.Manifest.Id.Trim());

        string? backup =
            null;

        if (Directory.Exists(
                destination))
        {
            if (IsReparsePoint(
                    destination))
            {
                throw new InvalidDataException(
                    "Installed extension destination cannot be a reparse point.");
            }

            backup =
                Path.Combine(
                    BackupRoot,
                    candidate.Manifest.Id.Trim()
                    + "-"
                    + DateTimeOffset.UtcNow.ToString(
                        "yyyyMMddHHmmss")
                    + "-"
                    + Guid.NewGuid().ToString(
                        "N"));

            Directory.Move(
                destination,
                backup);
        }

        try
        {
            Directory.Move(
                candidate.RootPath,
                destination);
        }
        catch
        {
            if (backup is not null
                && Directory.Exists(
                    backup)
                && !Directory.Exists(
                    destination))
            {
                Directory.Move(
                    backup,
                    destination);
            }

            throw;
        }

        return
            new ExtensionInstallResult(
                candidate.Manifest.Id.Trim(),
                destination,
                backup);
    }

    private void ValidateCandidateDirectory(
        string candidateRoot)
    {
        if (!Directory.Exists(
                candidateRoot))
        {
            throw new DirectoryNotFoundException(
                $"Extension candidate does not exist: {candidateRoot}");
        }

        if (IsReparsePoint(
                candidateRoot))
        {
            throw new InvalidDataException(
                "Extension candidate root cannot be a reparse point.");
        }

        var files =
            Directory.EnumerateFiles(
                    candidateRoot,
                    "*",
                    SearchOption.AllDirectories)
                .ToList();

        if (files.Count > 33)
        {
            throw new InvalidDataException(
                "Extension candidate contains too many files.");
        }

        foreach (var directory
                 in Directory.EnumerateDirectories(
                     candidateRoot,
                     "*",
                     SearchOption.AllDirectories))
        {
            if (IsReparsePoint(
                    directory))
            {
                throw new InvalidDataException(
                    "Extension candidate cannot contain reparse-point directories.");
            }
        }

        foreach (var file
                 in files)
        {
            if (IsReparsePoint(
                    file))
            {
                throw new InvalidDataException(
                    "Extension candidate cannot contain reparse-point files.");
            }

            var name =
                Path.GetFileName(
                    file);

            if (name.Equals(
                    "extension.json",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var extension =
                Path.GetExtension(
                    file);

            if (!AllowedTextExtensions.Contains(
                    extension))
            {
                throw new InvalidDataException(
                    $"Adaptive Platform v1 rejects executable/source payload '{name}'. Only declarative text assets are allowed.");
            }
        }

        if (!File.Exists(
                Path.Combine(
                    candidateRoot,
                    "extension.json")))
        {
            throw new InvalidDataException(
                "Extension candidate does not contain extension.json.");
        }
    }

    private static string ValidateRelativeTextPath(
        string relativePath)
    {
        if (string.IsNullOrWhiteSpace(
                relativePath))
        {
            throw new ArgumentException(
                "Extension file path cannot be empty.",
                nameof(relativePath));
        }

        var normalized =
            relativePath.Trim();

        if (Path.IsPathRooted(
                normalized))
        {
            throw new InvalidDataException(
                "Extension file path must be relative.");
        }

        if (Path.GetFileName(
                normalized)
            .Equals(
                "extension.json",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "extension.json is generated from the validated manifest and cannot be replaced by candidate text files.");
        }

        var extension =
            Path.GetExtension(
                normalized);

        if (!AllowedTextExtensions.Contains(
                extension))
        {
            throw new InvalidDataException(
                $"Extension file '{normalized}' is not an allowed declarative text asset.");
        }

        return normalized;
    }

    private static string ResolveInside(
        string root,
        string relativePath)
    {
        var full =
            Path.GetFullPath(
                Path.Combine(
                    root,
                    relativePath));

        EnsureInside(
            root,
            full);

        return full;
    }

    private static void EnsureInside(
        string root,
        string path)
    {
        var normalizedRoot =
            Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(
                    root))
            + Path.DirectorySeparatorChar;

        var normalizedPath =
            Path.GetFullPath(
                path);

        if (!normalizedPath.StartsWith(
                normalizedRoot,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Extension path escapes the controlled extension root.");
        }
    }

    private static void EnsureControlledDirectory(
        string path)
    {
        Directory.CreateDirectory(
            path);

        if (IsReparsePoint(
                path))
        {
            throw new InvalidDataException(
                $"Controlled extension directory cannot be a reparse point: {path}");
        }
    }

    private static bool IsReparsePoint(
        string path)
    {
        return
            (File.GetAttributes(
                 path)
             & FileAttributes.ReparsePoint)
            != 0;
    }

    private static void TryDeleteDirectory(
        string path)
    {
        try
        {
            if (Directory.Exists(
                    path))
            {
                Directory.Delete(
                    path,
                    true);
            }
        }
        catch
        {
            // Best effort candidate cleanup.
        }
    }
}
