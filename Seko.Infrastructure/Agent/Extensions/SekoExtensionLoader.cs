using System.Text.Json;
using Seko.Infrastructure.Agent.Permissions;

namespace Seko.Infrastructure.Agent.Extensions;

public sealed class SekoExtensionLoader
{
    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            PropertyNameCaseInsensitive =
                true
        };

    private readonly SekoExtensionValidator _validator =
        new();

    private readonly string _workspaceRoot;
    private readonly string _globalRoot;

    public SekoExtensionLoader(
        string workspaceRoot,
        string? globalRoot = null)
    {
        _workspaceRoot =
            Path.GetFullPath(
                workspaceRoot);

        _globalRoot =
            Path.GetFullPath(
                string.IsNullOrWhiteSpace(
                    globalRoot)
                    ? Path.Combine(
                        Environment.GetFolderPath(
                            Environment.SpecialFolder.LocalApplicationData),
                        "Seko",
                        "Extensions",
                        "Installed")
                    : globalRoot);
    }

    public SekoExtensionCatalog Load()
    {
        var packages =
            new List<SekoExtensionPackage>();

        var issues =
            new List<SekoExtensionLoadIssue>();

        var seenIds =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        LoadRoot(
            _globalRoot,
            CapabilitySource.Extension,
            seenIds,
            packages,
            issues);

        LoadRoot(
            Path.Combine(
                _workspaceRoot,
                ".seko",
                "extensions"),
            CapabilitySource.Project,
            seenIds,
            packages,
            issues);

        return
            new SekoExtensionCatalog(
                packages.AsReadOnly(),
                issues.AsReadOnly());
    }

    private void LoadRoot(
        string root,
        CapabilitySource source,
        ISet<string> seenIds,
        ICollection<SekoExtensionPackage> packages,
        ICollection<SekoExtensionLoadIssue> issues)
    {
        List<string> directories;

        try
        {
            if (!Directory.Exists(
                    root))
            {
                return;
            }

            if (IsReparsePoint(
                    root))
            {
                issues.Add(
                    new SekoExtensionLoadIssue(
                        root,
                        "Extension root cannot be a reparse point."));

                return;
            }

            directories =
                Directory.EnumerateDirectories(
                        root,
                        "*",
                        SearchOption.TopDirectoryOnly)
                    .OrderBy(
                        path => path,
                        StringComparer.OrdinalIgnoreCase)
                    .Take(
                        129)
                    .ToList();
        }
        catch (Exception exception)
            when (exception
                  is IOException
                  or UnauthorizedAccessException)
        {
            issues.Add(
                new SekoExtensionLoadIssue(
                    root,
                    exception.Message));

            return;
        }

        if (directories.Count > 128)
        {
            issues.Add(
                new SekoExtensionLoadIssue(
                    root,
                    "Extension root contains more than 128 packages. Extra packages were ignored."));

            directories.RemoveAt(
                directories.Count - 1);
        }

        foreach (var directory
                 in directories)
        {
            var manifestPath =
                Path.Combine(
                    directory,
                    "extension.json");

            if (!File.Exists(
                    manifestPath))
            {
                continue;
            }

            try
            {
                if (IsReparsePoint(
                        directory)
                    || IsReparsePoint(
                        manifestPath))
                {
                    throw new InvalidDataException(
                        "Extension manifests cannot be loaded through reparse points.");
                }

                if (new FileInfo(
                        manifestPath).Length > 512_000)
                {
                    throw new InvalidDataException(
                        "Extension manifest is too large.");
                }

                var manifest =
                    JsonSerializer.Deserialize<SekoExtensionManifest>(
                        File.ReadAllText(
                            manifestPath),
                        JsonOptions)
                    ?? throw new InvalidDataException(
                        "Extension manifest is empty.");

                var validationErrors =
                    _validator.Validate(
                        manifest);

                if (validationErrors.Count > 0)
                {
                    throw new InvalidDataException(
                        string.Join(
                            " ",
                            validationErrors));
                }

                if (!seenIds.Add(
                        manifest.Id.Trim()))
                {
                    throw new InvalidDataException(
                        $"Duplicate extension id '{manifest.Id}'.");
                }

                packages.Add(
                    new SekoExtensionPackage(
                        manifest,
                        Path.GetFullPath(
                            directory),
                        source));
            }
            catch (Exception exception)
                when (exception
                      is IOException
                      or UnauthorizedAccessException
                      or JsonException
                      or InvalidDataException)
            {
                issues.Add(
                    new SekoExtensionLoadIssue(
                        manifestPath,
                        exception.Message));
            }
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
}
