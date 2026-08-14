using System.Text.Json;

namespace Seko.Infrastructure.Agent.Projects;

public sealed class SekoProjectDetector
{
    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            PropertyNameCaseInsensitive =
                true
        };

    public SekoProjectProfile Detect(
        string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(
                workspaceRoot))
        {
            throw new ArgumentException(
                "Workspace root cannot be empty.",
                nameof(workspaceRoot));
        }

        var root =
            Path.GetFullPath(
                workspaceRoot);

        if (!Directory.Exists(
                root))
        {
            throw new DirectoryNotFoundException(
                $"Workspace root does not exist: {root}");
        }

        var technologies =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        var requiredAbilities =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase)
            {
                "filesystem.read"
            };

        var preferredCapabilities =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        var enabledSkills =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        var projectType =
            "General";

        DetectBuiltInSignals(
            root,
            technologies,
            requiredAbilities,
            preferredCapabilities,
            ref projectType);

        var name =
            new DirectoryInfo(
                root)
                .Name;

        string? configWarning =
            null;

        var config =
            LoadConfig(
                root,
                out configWarning);

        if (config is not null)
        {
            var configuredName =
                config.Name?.Trim();

            if (!string.IsNullOrWhiteSpace(
                    configuredName))
            {
                name =
                    configuredName;
            }

            var configuredType =
                config.Type?.Trim();

            if (!string.IsNullOrWhiteSpace(
                    configuredType))
            {
                projectType =
                    configuredType;
            }

            AddValues(
                technologies,
                config.Technologies);

            AddValues(
                requiredAbilities,
                config.RequiredAbilities);

            AddValues(
                preferredCapabilities,
                config.PreferredCapabilities);

            AddValues(
                enabledSkills,
                config.EnabledSkills);
        }

        return
            new SekoProjectProfile(
                root,
                name,
                projectType,
                technologies
                    .OrderBy(
                        value => value,
                        StringComparer.OrdinalIgnoreCase)
                    .ToList()
                    .AsReadOnly(),
                requiredAbilities
                    .OrderBy(
                        value => value,
                        StringComparer.OrdinalIgnoreCase)
                    .ToList()
                    .AsReadOnly(),
                preferredCapabilities
                    .OrderBy(
                        value => value,
                        StringComparer.OrdinalIgnoreCase)
                    .ToList()
                    .AsReadOnly(),
                enabledSkills
                    .OrderBy(
                        value => value,
                        StringComparer.OrdinalIgnoreCase)
                    .ToList()
                    .AsReadOnly(),
                configWarning);
    }

    private static void DetectBuiltInSignals(
        string root,
        ISet<string> technologies,
        ISet<string> requiredAbilities,
        ISet<string> preferredCapabilities,
        ref string projectType)
    {
        if (Directory.Exists(
                Path.Combine(
                    root,
                    ".git")))
        {
            technologies.Add(
                "Git");

            preferredCapabilities.Add(
                "source-control.git");
        }

        var hasDotNet =
            Directory.EnumerateFiles(
                    root,
                    "*.sln",
                    SearchOption.TopDirectoryOnly)
                .Any()
            || Directory.EnumerateFiles(
                    root,
                    "*.csproj",
                    SearchOption.TopDirectoryOnly)
                .Any();

        if (hasDotNet)
        {
            technologies.Add(
                ".NET");

            requiredAbilities.Add(
                "project.build");

            preferredCapabilities.Add(
                "build.dotnet");

            projectType =
                "Software";
        }

        var packageJson =
            Path.Combine(
                root,
                "package.json");

        if (File.Exists(
                packageJson))
        {
            technologies.Add(
                "Node.js");

            projectType =
                "Web";
        }

        if (File.Exists(
                Path.Combine(
                    root,
                    "pyproject.toml"))
            || File.Exists(
                Path.Combine(
                    root,
                    "requirements.txt")))
        {
            technologies.Add(
                "Python");

            if (projectType
                == "General")
            {
                projectType =
                    "Software";
            }
        }

        if (File.Exists(
                Path.Combine(
                    root,
                    "Cargo.toml")))
        {
            technologies.Add(
                "Rust");

            projectType =
                "Software";
        }

        if (File.Exists(
                Path.Combine(
                    root,
                    "go.mod")))
        {
            technologies.Add(
                "Go");

            projectType =
                "Software";
        }

        if (Directory.Exists(
                Path.Combine(
                    root,
                    "Assets"))
            && File.Exists(
                Path.Combine(
                    root,
                    "ProjectSettings",
                    "ProjectVersion.txt")))
        {
            technologies.Add(
                "Unity");

            projectType =
                "Game";

            preferredCapabilities.Add(
                "engine.unity");
        }

        if (Directory.EnumerateFiles(
                root,
                "*.uproject",
                SearchOption.TopDirectoryOnly)
            .Any())
        {
            technologies.Add(
                "Unreal Engine");

            projectType =
                "Game";

            preferredCapabilities.Add(
                "engine.unreal");
        }

        if (File.Exists(
                Path.Combine(
                    root,
                    "project.godot")))
        {
            technologies.Add(
                "Godot");

            projectType =
                "Game";

            preferredCapabilities.Add(
                "engine.godot");
        }

        if (Directory.EnumerateFiles(
                root,
                "*.blend",
                SearchOption.TopDirectoryOnly)
            .Any())
        {
            technologies.Add(
                "Blender");

            if (projectType
                == "General")
            {
                projectType =
                    "3D";
            }

            preferredCapabilities.Add(
                "3d.blender");
        }
    }

    private static SekoProjectConfig? LoadConfig(
        string root,
        out string? warning)
    {
        warning =
            null;

        var path =
            Path.Combine(
                root,
                ".seko",
                "project.json");

        if (!File.Exists(
                path))
        {
            return null;
        }

        try
        {
            if (new FileInfo(
                    path).Length > 256_000)
            {
                throw new InvalidDataException(
                    ".seko/project.json is too large.");
            }

            var json =
                File.ReadAllText(
                    path);

            var config =
                JsonSerializer.Deserialize<SekoProjectConfig>(
                    json,
                    JsonOptions);

            if (config is null)
            {
                throw new InvalidDataException(
                    "Project configuration is empty.");
            }

            if (config.Version != 1)
            {
                throw new InvalidDataException(
                    $"Unsupported .seko project configuration version '{config.Version}'.");
            }

            return config;
        }
        catch (Exception exception)
            when (exception
                  is IOException
                  or UnauthorizedAccessException
                  or JsonException
                  or InvalidDataException)
        {
            warning =
                "Could not load .seko/project.json: "
                + exception.Message;

            return null;
        }
    }

    private static void AddValues(
        ISet<string> destination,
        IEnumerable<string>? values)
    {
        if (values is null)
        {
            return;
        }

        foreach (var value
                 in values)
        {
            if (!string.IsNullOrWhiteSpace(
                    value))
            {
                destination.Add(
                    value.Trim());
            }
        }
    }
}
