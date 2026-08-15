using System.Text.Json;
using Seko.Core.Workspaces;
using Seko.Infrastructure.Agent;

namespace Seko.Tests.Agent;

public sealed class SekoToolHostSafetyRegressionTests
{
    [Fact]
    public async Task ReadFile_PathEscape_IsBlocked()
    {
        using var temporaryWorkspace =
            new TemporaryWorkspace();

        var host =
            await CreateHostAsync(
                temporaryWorkspace);

        var escapePath =
            ".."
            + Path.DirectorySeparatorChar
            + "outside.txt";

        var result =
            await host.ExecuteAsync(
                "read_file",
                JsonSerializer.Serialize(
                    new
                    {
                        path = escapePath
                    }));

        Assert.StartsWith(
            "ERROR:",
            result);

        Assert.Contains(
            "Path escapes the active workspace",
            result);
    }

    [Fact]
    public async Task ReadFile_SensitiveFile_IsBlocked()
    {
        using var temporaryWorkspace =
            new TemporaryWorkspace();

        await File.WriteAllTextAsync(
            Path.Combine(
                temporaryWorkspace.RootPath,
                ".env"),
            "API_KEY=should-not-be-readable");

        var host =
            await CreateHostAsync(
                temporaryWorkspace);

        var result =
            await host.ExecuteAsync(
                "read_file",
                JsonSerializer.Serialize(
                    new
                    {
                        path = ".env"
                    }));

        Assert.StartsWith(
            "ERROR:",
            result);

        Assert.Contains(
            "sensitive",
            result,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WriteFile_NewSourceOutsideProject_IsBlocked()
    {
        using var temporaryWorkspace =
            new TemporaryWorkspace();

        CreateProject(
            temporaryWorkspace.RootPath);

        var host =
            await CreateHostAsync(
                temporaryWorkspace);

        var result =
            await host.ExecuteAsync(
                "write_file",
                JsonSerializer.Serialize(
                    new
                    {
                        path = "Orphan.cs",
                        content = "namespace Orphan; public sealed class Example { }"
                    }));

        Assert.StartsWith(
            "ERROR:",
            result);

        Assert.Contains(
            "SOURCE_PATH_NOT_IN_PROJECT",
            result);

        Assert.False(
            File.Exists(
                Path.Combine(
                    temporaryWorkspace.RootPath,
                    "Orphan.cs")));
    }

    [Fact]
    public async Task ReplaceText_ExistingSourceOutsideProject_IsBlocked()
    {
        using var temporaryWorkspace =
            new TemporaryWorkspace();

        CreateProject(
            temporaryWorkspace.RootPath);

        var orphanPath =
            Path.Combine(
                temporaryWorkspace.RootPath,
                "Orphan.cs");

        await File.WriteAllTextAsync(
            orphanPath,
            "namespace Orphan; public sealed class Before { }");

        var host =
            await CreateHostAsync(
                temporaryWorkspace);

        var result =
            await host.ExecuteAsync(
                "replace_text",
                JsonSerializer.Serialize(
                    new
                    {
                        path = "Orphan.cs",
                        old_text = "Before",
                        new_text = "After"
                    }));

        Assert.StartsWith(
            "ERROR:",
            result);

        Assert.Contains(
            "SOURCE_PATH_NOT_IN_PROJECT",
            result);

        var content =
            await File.ReadAllTextAsync(
                orphanPath);

        Assert.Contains(
            "Before",
            content);

        Assert.DoesNotContain(
            "After",
            content);
    }

    [Fact]
    public async Task WriteFile_SourceInsideProject_IsAllowed()
    {
        using var temporaryWorkspace =
            new TemporaryWorkspace();

        var projectDirectory =
            CreateProject(
                temporaryWorkspace.RootPath);

        var host =
            await CreateHostAsync(
                temporaryWorkspace);

        var result =
            await host.ExecuteAsync(
                "write_file",
                JsonSerializer.Serialize(
                    new
                    {
                        path = Path.Combine(
                            "App",
                            "Good.cs"),
                        content = "namespace App; public sealed class Good { }"
                    }));

        Assert.StartsWith(
            "Wrote ",
            result);

        Assert.True(
            File.Exists(
                Path.Combine(
                    projectDirectory,
                    "Good.cs")));
    }

    [Fact]
    public async Task WriteFile_NewProjectFile_IsBlocked()
    {
        using var temporaryWorkspace =
            new TemporaryWorkspace();

        CreateProject(
            temporaryWorkspace.RootPath);

        var host =
            await CreateHostAsync(
                temporaryWorkspace);

        var result =
            await host.ExecuteAsync(
                "write_file",
                JsonSerializer.Serialize(
                    new
                    {
                        path = Path.Combine(
                            "Other",
                            "Other.csproj"),
                        content = "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>"
                    }));

        Assert.StartsWith(
            "ERROR:",
            result);

        Assert.Contains(
            "NEW_PROJECT_FILE_REQUIRES_EXPLICIT_SUPPORT",
            result);
    }

    [Fact]
    public void ToolDefinitions_ContainCoreSafetyAndDevelopmentTools()
    {
        using var temporaryWorkspace =
            new TemporaryWorkspace();

        var host =
            new SekoToolHost(
                temporaryWorkspace.Workspace);

        var definitions =
            host.CreateToolDefinitions();

        var names =
            definitions
                .Select(
                    definition =>
                        definition?["function"]?["name"]?.GetValue<string>())
                .Where(
                    name =>
                        !string.IsNullOrWhiteSpace(
                            name))
                .Cast<string>()
                .ToHashSet(
                    StringComparer.Ordinal);

        var expectedTools =
            new[]
            {
                "search_workspace",
                "find_files",
                "find_text",
                "list_files",
                "read_file",
                "verify_file",
                "read_task_log",
                "write_file",
                "replace_text",
                "build_project",
                "git_status",
                "git_diff"
            };

        foreach (var expectedTool
                 in expectedTools)
        {
            Assert.Contains(
                expectedTool,
                names);
        }
    }

    private static async Task<SekoToolHost> CreateHostAsync(
        TemporaryWorkspace temporaryWorkspace)
    {
        var host =
            new SekoToolHost(
                temporaryWorkspace.Workspace);

        await host.BeginTaskAsync();

        return host;
    }

    private static string CreateProject(
        string workspaceRoot)
    {
        var projectDirectory =
            Path.Combine(
                workspaceRoot,
                "App");

        Directory.CreateDirectory(
            projectDirectory);

        File.WriteAllText(
            Path.Combine(
                projectDirectory,
                "App.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        return projectDirectory;
    }

    private sealed class TemporaryWorkspace :
        IDisposable
    {
        public string RootPath
        {
            get;
        }

        public Workspace Workspace
        {
            get;
        }

        public TemporaryWorkspace()
        {
            RootPath =
                Path.Combine(
                    Path.GetTempPath(),
                    "Seko.Tests",
                    Guid.NewGuid()
                        .ToString("N"));

            Directory.CreateDirectory(
                RootPath);

            Workspace =
                new Workspace
                {
                    Id =
                        Guid.NewGuid(),

                    Name =
                        "Regression Test Workspace",

                    RootPath =
                        RootPath
                };
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(
                        RootPath))
                {
                    Directory.Delete(
                        RootPath,
                        true);
                }
            }
            catch
            {
                // A failed cleanup should not hide the test result.
            }
        }
    }
}
