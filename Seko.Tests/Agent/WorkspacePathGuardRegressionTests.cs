using Seko.Infrastructure.Agent.Safety;

namespace Seko.Tests.Agent;

public sealed class WorkspacePathGuardRegressionTests
{
    [Fact]
    public void ResolveSafePath_RelativePath_StaysInsideWorkspace()
    {
        using var workspace =
            new TemporaryWorkspace();

        var guard =
            new WorkspacePathGuard(
                workspace.RootPath);

        var resolved =
            guard.ResolveSafePath(
                Path.Combine(
                    "App",
                    "Example.cs"));

        Assert.True(
            resolved.StartsWith(
                workspace.RootPath,
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ResolveSafePath_ParentEscape_IsBlocked()
    {
        using var workspace =
            new TemporaryWorkspace();

        var guard =
            new WorkspacePathGuard(
                workspace.RootPath);

        Assert.Throws<UnauthorizedAccessException>(
            () =>
                guard.ResolveSafePath(
                    ".."
                    + Path.DirectorySeparatorChar
                    + "outside.txt"));
    }

    [Fact]
    public void ResolveSafePath_AbsolutePath_IsBlocked()
    {
        using var workspace =
            new TemporaryWorkspace();

        var guard =
            new WorkspacePathGuard(
                workspace.RootPath);

        var absolutePath =
            Path.Combine(
                workspace.RootPath,
                "Example.cs");

        Assert.Throws<UnauthorizedAccessException>(
            () =>
                guard.ResolveSafePath(
                    absolutePath));
    }

    [Fact]
    public void ResolveSafePath_IgnoredDirectory_IsBlocked()
    {
        using var workspace =
            new TemporaryWorkspace();

        var guard =
            new WorkspacePathGuard(
                workspace.RootPath);

        Assert.Throws<UnauthorizedAccessException>(
            () =>
                guard.ResolveSafePath(
                    Path.Combine(
                        ".git",
                        "config")));
    }

    [Theory]
    [InlineData(".env")]
    [InlineData(".env.local")]
    [InlineData("secrets.json")]
    [InlineData("credentials.json")]
    [InlineData("certificate.pfx")]
    [InlineData("private.key")]
    public void EnsureAllowedFile_SensitiveFile_IsBlocked(
        string fileName)
    {
        using var workspace =
            new TemporaryWorkspace();

        var guard =
            new WorkspacePathGuard(
                workspace.RootPath);

        var fullPath =
            Path.Combine(
                workspace.RootPath,
                fileName);

        Assert.Throws<UnauthorizedAccessException>(
            () =>
                guard.EnsureAllowedFile(
                    fullPath));
    }

    [Fact]
    public void EnsureAllowedFile_UnsupportedExtension_IsBlocked()
    {
        using var workspace =
            new TemporaryWorkspace();

        var guard =
            new WorkspacePathGuard(
                workspace.RootPath);

        var fullPath =
            Path.Combine(
                workspace.RootPath,
                "program.exe");

        Assert.Throws<InvalidOperationException>(
            () =>
                guard.EnsureAllowedFile(
                    fullPath));
    }

    [Fact]
    public void EnsureAllowedFile_GitIgnore_IsAllowed()
    {
        using var workspace =
            new TemporaryWorkspace();

        var guard =
            new WorkspacePathGuard(
                workspace.RootPath);

        guard.EnsureAllowedFile(
            Path.Combine(
                workspace.RootPath,
                ".gitignore"));
    }

    [Fact]
    public void SourceInsideDiscoveredProject_IsAllowed()
    {
        using var workspace =
            new TemporaryWorkspace();

        var projectDirectory =
            workspace.CreateProject();

        var guard =
            new WorkspacePathGuard(
                workspace.RootPath);

        guard.EnsureSourceModificationBelongsToProject(
            Path.Combine(
                projectDirectory,
                "NewSource.cs"));
    }

    [Fact]
    public void SourceOutsideDiscoveredProject_IsBlocked()
    {
        using var workspace =
            new TemporaryWorkspace();

        workspace.CreateProject();

        var guard =
            new WorkspacePathGuard(
                workspace.RootPath);

        var exception =
            Assert.Throws<InvalidOperationException>(
                () =>
                    guard.EnsureSourceModificationBelongsToProject(
                        Path.Combine(
                            workspace.RootPath,
                            "Orphan.cs")));

        Assert.Contains(
            "SOURCE_PATH_NOT_IN_PROJECT",
            exception.Message);
    }

    [Fact]
    public void ExistingSourceOutsideDiscoveredProject_IsBlocked()
    {
        using var workspace =
            new TemporaryWorkspace();

        workspace.CreateProject();

        var orphanPath =
            Path.Combine(
                workspace.RootPath,
                "Orphan.cs");

        File.WriteAllText(
            orphanPath,
            "namespace Orphan; public sealed class Example { }");

        var guard =
            new WorkspacePathGuard(
                workspace.RootPath);

        var exception =
            Assert.Throws<InvalidOperationException>(
                () =>
                    guard.EnsureSourceModificationBelongsToProject(
                        orphanPath));

        Assert.Contains(
            "SOURCE_PATH_NOT_IN_PROJECT",
            exception.Message);
    }

    [Fact]
    public void NewProjectFileThroughGenericWritePolicy_IsBlocked()
    {
        using var workspace =
            new TemporaryWorkspace();

        var projectDirectory =
            workspace.CreateProject();

        var guard =
            new WorkspacePathGuard(
                workspace.RootPath);

        var exception =
            Assert.Throws<InvalidOperationException>(
                () =>
                    guard.EnsureSourceModificationBelongsToProject(
                        Path.Combine(
                            projectDirectory,
                            "Second.csproj")));

        Assert.Contains(
            "NEW_PROJECT_FILE_REQUIRES_EXPLICIT_SUPPORT",
            exception.Message);
    }

    [Fact]
    public void SearchableFilePolicy_UsesExpectedTypes()
    {
        using var workspace =
            new TemporaryWorkspace();

        var guard =
            new WorkspacePathGuard(
                workspace.RootPath);

        Assert.True(
            guard.IsSearchableFile(
                Path.Combine(
                    workspace.RootPath,
                    "Example.cs")));

        Assert.True(
            guard.IsSearchableFile(
                Path.Combine(
                    workspace.RootPath,
                    ".gitignore")));

        Assert.False(
            guard.IsSearchableFile(
                Path.Combine(
                    workspace.RootPath,
                    "program.exe")));
    }

    [Fact]
    public void WorkspaceEnumeration_SkipsIgnoredDirectories()
    {
        using var workspace =
            new TemporaryWorkspace();

        var visibleDirectory =
            Path.Combine(
                workspace.RootPath,
                "Visible");

        var ignoredDirectory =
            Path.Combine(
                workspace.RootPath,
                "bin");

        Directory.CreateDirectory(
            visibleDirectory);

        Directory.CreateDirectory(
            ignoredDirectory);

        File.WriteAllText(
            Path.Combine(
                visibleDirectory,
                "Visible.cs"),
            "visible");

        File.WriteAllText(
            Path.Combine(
                ignoredDirectory,
                "Hidden.cs"),
            "hidden");

        var guard =
            new WorkspacePathGuard(
                workspace.RootPath);

        var files =
            guard.EnumerateWorkspaceFiles(
                    100)
                .Select(
                    Path.GetFileName)
                .ToList();

        Assert.Contains(
            "Visible.cs",
            files);

        Assert.DoesNotContain(
            "Hidden.cs",
            files);
    }

    private sealed class TemporaryWorkspace :
        IDisposable
    {
        public string RootPath
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
        }

        public string CreateProject()
        {
            var projectDirectory =
                Path.Combine(
                    RootPath,
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
                // Cleanup must not hide the actual test result.
            }
        }
    }
}
