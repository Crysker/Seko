using Seko.Core.Workspaces;
using Seko.Infrastructure.Agent.Build;
using Seko.Infrastructure.Agent.Safety;

namespace Seko.Tests.Agent;

public sealed class BuildServiceRegressionTests
{
    [Fact]
    public void FindBuildTarget_PrefersRootSolution()
    {
        using var temporaryWorkspace =
            new TemporaryWorkspace();

        temporaryWorkspace.CreateProject(
            "App");

        var solutionPath =
            Path.Combine(
                temporaryWorkspace.RootPath,
                "Seko.sln");

        File.WriteAllText(
            solutionPath,
            "Microsoft Visual Studio Solution File, Format Version 12.00");

        var service =
            temporaryWorkspace.CreateBuildService();

        Assert.Equal(
            solutionPath,
            service.FindBuildTarget());
    }

    [Fact]
    public void FindBuildTarget_FallsBackToProject()
    {
        using var temporaryWorkspace =
            new TemporaryWorkspace();

        var projectPath =
            temporaryWorkspace.CreateProject(
                "App");

        var service =
            temporaryWorkspace.CreateBuildService();

        Assert.Equal(
            projectPath,
            service.FindBuildTarget());
    }

    [Fact]
    public void FindBuildTarget_IgnoresGeneratedDirectories()
    {
        using var temporaryWorkspace =
            new TemporaryWorkspace();

        var binDirectory =
            Path.Combine(
                temporaryWorkspace.RootPath,
                "bin");

        Directory.CreateDirectory(
            binDirectory);

        File.WriteAllText(
            Path.Combine(
                binDirectory,
                "Hidden.csproj"),
            MinimalProject);

        var service =
            temporaryWorkspace.CreateBuildService();

        Assert.Null(
            service.FindBuildTarget());
    }

    [Fact]
    public async Task BuildAsync_NoTarget_ReturnsNoTarget()
    {
        using var temporaryWorkspace =
            new TemporaryWorkspace();

        var service =
            temporaryWorkspace.CreateBuildService();

        var result =
            await service.BuildAsync();

        Assert.False(
            result.HasTarget);

        Assert.False(
            result.Succeeded);

        Assert.Equal(
            -1,
            result.ExitCode);
    }

    [Fact]
    public async Task BuildAsync_ValidProject_Succeeds()
    {
        using var temporaryWorkspace =
            new TemporaryWorkspace();

        var projectPath =
            temporaryWorkspace.CreateProject(
                "Valid");

        File.WriteAllText(
            Path.Combine(
                Path.GetDirectoryName(
                    projectPath)!,
                "Example.cs"),
            "namespace BuildCheck; public sealed class Example { }");

        var service =
            temporaryWorkspace.CreateBuildService();

        var result =
            await service.BuildAsync();

        Assert.True(
            result.HasTarget);

        Assert.True(
            result.Succeeded);

        Assert.Equal(
            0,
            result.ExitCode);

        Assert.Equal(
            projectPath,
            result.TargetPath);
    }

    [Fact]
    public async Task BuildAsync_CompilerError_Fails()
    {
        using var temporaryWorkspace =
            new TemporaryWorkspace();

        var projectPath =
            temporaryWorkspace.CreateProject(
                "Broken");

        File.WriteAllText(
            Path.Combine(
                Path.GetDirectoryName(
                    projectPath)!,
                "Broken.cs"),
            "namespace BuildCheck; public sealed class Broken { this is not valid C# }");

        var service =
            temporaryWorkspace.CreateBuildService();

        var result =
            await service.BuildAsync();

        Assert.True(
            result.HasTarget);

        Assert.False(
            result.Succeeded);

        Assert.NotEqual(
            0,
            result.ExitCode);

        Assert.Equal(
            projectPath,
            result.TargetPath);
    }

    private const string MinimalProject =
        """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net8.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
          </PropertyGroup>
        </Project>
        """;

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
                    "BuildService",
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
                        "Build Service Test",

                    RootPath =
                        RootPath
                };
        }

        public string CreateProject(
            string directoryName)
        {
            var projectDirectory =
                Path.Combine(
                    RootPath,
                    directoryName);

            Directory.CreateDirectory(
                projectDirectory);

            var projectPath =
                Path.Combine(
                    projectDirectory,
                    $"{directoryName}.csproj");

            File.WriteAllText(
                projectPath,
                MinimalProject);

            return projectPath;
        }

        public BuildService CreateBuildService()
        {
            var pathGuard =
                new WorkspacePathGuard(
                    RootPath);

            return
                new BuildService(
                    Workspace,
                    pathGuard);
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
