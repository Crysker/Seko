using System.Text.Json;
using Seko.Core.Workspaces;
using Seko.Infrastructure.Agent;

namespace Seko.Tests.Agent;

public sealed class NonBuildArtifactVerificationRegressionTests
{
    [Fact]
    public async Task ValidJsonModification_IsVerifiedDeterministically()
    {
        using var workspace =
            new TemporaryWorkspace();

        var host =
            await CreateHostAsync(
                workspace);

        var writeResult =
            await host.ExecuteAsync(
                "write_file",
                JsonSerializer.Serialize(
                    new
                    {
                        path = "settings.json",
                        content = "{\"name\":\"Seko\",\"enabled\":true}"
                    }));

        Assert.StartsWith(
            "Wrote ",
            writeResult);

        var verification =
            await VerifyFileAsync(
                host,
                "settings.json");

        Assert.StartsWith(
            "VERIFICATION PASSED:",
            verification);

        Assert.Contains(
            "persistence=exact",
            verification);

        Assert.Contains(
            "structure=json",
            verification);
    }

    [Fact]
    public async Task MalformedJsonModification_FailsVerification()
    {
        using var workspace =
            new TemporaryWorkspace();

        var host =
            await CreateHostAsync(
                workspace);

        await host.ExecuteAsync(
            "write_file",
            JsonSerializer.Serialize(
                new
                {
                    path = "settings.json",
                    content = "{\"name\": }"
                }));

        var verification =
            await VerifyFileAsync(
                host,
                "settings.json");

        Assert.StartsWith(
            "ERROR: VERIFICATION_FAILED.",
            verification);

        Assert.Contains(
            "Malformed JSON",
            verification,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task XmlConfigModification_IsParsedWhenXmlShaped()
    {
        using var workspace =
            new TemporaryWorkspace();

        var host =
            await CreateHostAsync(
                workspace);

        await host.ExecuteAsync(
            "write_file",
            JsonSerializer.Serialize(
                new
                {
                    path = "app.config",
                    content =
                        "<configuration><value>ok</value></configuration>"
                }));

        var verification =
            await VerifyFileAsync(
                host,
                "app.config");

        Assert.StartsWith(
            "VERIFICATION PASSED:",
            verification);

        Assert.Contains(
            "structure=xml",
            verification);
    }

    [Fact]
    public async Task MalformedXmlConfigModification_FailsVerification()
    {
        using var workspace =
            new TemporaryWorkspace();

        var host =
            await CreateHostAsync(
                workspace);

        await host.ExecuteAsync(
            "write_file",
            JsonSerializer.Serialize(
                new
                {
                    path = "app.config",
                    content =
                        "<configuration><value></configuration>"
                }));

        var verification =
            await VerifyFileAsync(
                host,
                "app.config");

        Assert.StartsWith(
            "ERROR: VERIFICATION_FAILED.",
            verification);

        Assert.Contains(
            "Malformed XML",
            verification,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(
        "README.md",
        "# Seko\n\nVerification persisted.")]
    [InlineData(
        "notes.txt",
        "plain text persisted")]
    [InlineData(
        "settings.toml",
        "name = \"Seko\"")]
    [InlineData(
        "settings.ini",
        "[seko]\nenabled=true")]
    public async Task TextAndConfigArtifacts_VerifyExactPersistence(
        string path,
        string content)
    {
        using var workspace =
            new TemporaryWorkspace();

        var host =
            await CreateHostAsync(
                workspace);

        await host.ExecuteAsync(
            "write_file",
            JsonSerializer.Serialize(
                new
                {
                    path,
                    content
                }));

        var verification =
            await VerifyFileAsync(
                host,
                path);

        Assert.StartsWith(
            "VERIFICATION PASSED:",
            verification);

        Assert.Contains(
            "persistence=exact",
            verification);

        Assert.Contains(
            "structure=text",
            verification);
    }

    [Fact]
    public async Task ReplaceText_VerifiesExactPostEditContent()
    {
        using var workspace =
            new TemporaryWorkspace();

        var filePath =
            Path.Combine(
                workspace.RootPath,
                "notes.txt");

        await File.WriteAllTextAsync(
            filePath,
            "before");

        var host =
            await CreateHostAsync(
                workspace);

        var replaceResult =
            await host.ExecuteAsync(
                "replace_text",
                JsonSerializer.Serialize(
                    new
                    {
                        path = "notes.txt",
                        old_text = "before",
                        new_text = "after"
                    }));

        Assert.StartsWith(
            "Updated ",
            replaceResult);

        var verification =
            await VerifyFileAsync(
                host,
                "notes.txt");

        Assert.StartsWith(
            "VERIFICATION PASSED:",
            verification);

        Assert.Equal(
            "after",
            await File.ReadAllTextAsync(
                filePath));
    }

    [Fact]
    public async Task ExternalChangeAfterModification_InvalidatesVerification()
    {
        using var workspace =
            new TemporaryWorkspace();

        var host =
            await CreateHostAsync(
                workspace);

        await host.ExecuteAsync(
            "write_file",
            JsonSerializer.Serialize(
                new
                {
                    path = "notes.txt",
                    content = "expected final content"
                }));

        await File.WriteAllTextAsync(
            Path.Combine(
                workspace.RootPath,
                "notes.txt"),
            "changed outside the recorded modification");

        var verification =
            await VerifyFileAsync(
                host,
                "notes.txt");

        Assert.StartsWith(
            "ERROR: VERIFICATION_FAILED.",
            verification);

        Assert.Contains(
            "differs from the exact post-edit content",
            verification,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PreEditFileState_CannotVerifyModificationGeneration()
    {
        using var workspace =
            new TemporaryWorkspace();

        await File.WriteAllTextAsync(
            Path.Combine(
                workspace.RootPath,
                "README.md"),
            "existing content");

        var host =
            await CreateHostAsync(
                workspace);

        var verification =
            await VerifyFileAsync(
                host,
                "README.md");

        Assert.StartsWith(
            "ERROR: VERIFICATION_FAILED.",
            verification);

        Assert.Contains(
            "was not successfully modified by this task",
            verification,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildSuccess_DoesNotVerifyLatestNonBuildModification()
    {
        var setup =
            CreateVerificationState(
                "README.md");

        Assert.False(
            setup.State.LatestModificationRequiresBuild);

        var outcome =
            SekoAutonomyLiveLoop.ClassifyToolResult(
                setup.State,
                "build_project",
                "BUILD EXIT CODE: 0",
                toolSucceeded:
                    true);

        Assert.Equal(
            SekoAutonomyToolOutcomeKind.NoChange,
            outcome.Kind);

        Assert.Null(
            outcome.Signal);

        var decision =
            setup.Controller.ApplyToolOutcome(
                setup.State,
                outcome);

        Assert.Equal(
            SekoAutonomyPhase.Verification,
            decision.State.Phase);

        Assert.True(
            decision.State.VerifiedModificationGeneration
            < decision.State.ModificationGeneration);
    }

    [Fact]
    public void VerifyFileSuccess_VerifiesLatestNonBuildModification()
    {
        var setup =
            CreateVerificationState(
                "README.md");

        var outcome =
            SekoAutonomyLiveLoop.ClassifyToolResult(
                setup.State,
                "verify_file",
                "VERIFICATION PASSED: README.md",
                toolSucceeded:
                    true,
                argumentsJson:
                    JsonSerializer.Serialize(
                        new
                        {
                            path = "README.md"
                        }));

        Assert.Equal(
            SekoAutonomySignal.VerificationSucceeded,
            outcome.Signal);

        var decision =
            setup.Controller.ApplyToolOutcome(
                setup.State,
                outcome);

        Assert.Equal(
            SekoAutonomyPhase.Synthesis,
            decision.State.Phase);

        Assert.Equal(
            decision.State.ModificationGeneration,
            decision.State.VerifiedModificationGeneration);
    }

    [Fact]
    public void VerifyFileFailure_EntersBoundedRepairForNonBuildModification()
    {
        var setup =
            CreateVerificationState(
                "settings.json");

        var outcome =
            SekoAutonomyLiveLoop.ClassifyToolResult(
                setup.State,
                "verify_file",
                "ERROR: VERIFICATION_FAILED. Malformed JSON.",
                toolSucceeded:
                    false,
                argumentsJson:
                    JsonSerializer.Serialize(
                        new
                        {
                            path = "settings.json"
                        }));

        Assert.Equal(
            SekoAutonomySignal.VerificationFailed,
            outcome.Signal);

        var decision =
            setup.Controller.ApplyToolOutcome(
                setup.State,
                outcome);

        Assert.Equal(
            SekoAutonomyPhase.Repair,
            decision.State.Phase);

        Assert.Equal(
            1,
            decision.State.RepairCycles);

        Assert.Equal(
            setup.State.ModificationGeneration,
            decision.State.LastVerificationFailureGeneration);
    }

    [Fact]
    public void VerifyFileForOlderPath_DoesNotVerifyLatestGeneration()
    {
        var setup =
            CreateVerificationState(
                "latest.txt");

        var outcome =
            SekoAutonomyLiveLoop.ClassifyToolResult(
                setup.State,
                "verify_file",
                "VERIFICATION PASSED: older.txt",
                toolSucceeded:
                    true,
                argumentsJson:
                    JsonSerializer.Serialize(
                        new
                        {
                            path = "older.txt"
                        }));

        Assert.Equal(
            SekoAutonomyToolOutcomeKind.NoChange,
            outcome.Kind);

        Assert.Null(
            outcome.Signal);

        var decision =
            setup.Controller.ApplyToolOutcome(
                setup.State,
                outcome);

        Assert.Equal(
            SekoAutonomyPhase.Verification,
            decision.State.Phase);

        Assert.True(
            decision.State.VerifiedModificationGeneration
            < decision.State.ModificationGeneration);
    }

    [Fact]
    public void BuildStillVerifiesLatestBuildRelevantModification()
    {
        var setup =
            CreateVerificationState(
                "Program.cs");

        Assert.True(
            setup.State.LatestModificationRequiresBuild);

        var outcome =
            SekoAutonomyLiveLoop.ClassifyToolResult(
                setup.State,
                "build_project",
                "BUILD EXIT CODE: 0",
                toolSucceeded:
                    true);

        Assert.Equal(
            SekoAutonomySignal.VerificationSucceeded,
            outcome.Signal);

        var decision =
            setup.Controller.ApplyToolOutcome(
                setup.State,
                outcome);

        Assert.Equal(
            SekoAutonomyPhase.Synthesis,
            decision.State.Phase);

        Assert.Equal(
            decision.State.ModificationGeneration,
            decision.State.VerifiedModificationGeneration);
    }

    private static async Task<string> VerifyFileAsync(
        SekoToolHost host,
        string path)
    {
        return
            await host.ExecuteAsync(
                "verify_file",
                JsonSerializer.Serialize(
                    new
                    {
                        path
                    }));
    }

    private static (
        SekoAutonomyController Controller,
        SekoAutonomyState State)
        CreateVerificationState(
            string path)
    {
        var controller =
            SekoAutonomyLiveLoop.CreateController(
                new TaskIntent(
                    RequiresWorkspaceTools:
                        true,
                    RequiresModification:
                        true,
                    ExplicitBuildRequested:
                        false),
                requiresWebResearch:
                    false);

        var state =
            controller.Start(
                controller.CreateInitialState())
                .State;

        state =
            controller.ApplySignal(
                state,
                SekoAutonomySignal.InspectionCompleted)
                .State;

        var modification =
            SekoAutonomyLiveLoop.ApplyToolResult(
                controller,
                state,
                "replace_text",
                "Updated " + path + ".",
                toolSucceeded:
                    true,
                argumentsJson:
                    JsonSerializer.Serialize(
                        new
                        {
                            path,
                            old_text = "before",
                            new_text = "after"
                        }));

        Assert.Equal(
            SekoAutonomyPhase.Verification,
            modification.State.Phase);

        return
            (controller, modification.State);
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
                    "Seko.NonBuildVerification.Tests",
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
                        "Non-build verification test",

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
                // Cleanup must not hide the regression result.
            }
        }
    }
}