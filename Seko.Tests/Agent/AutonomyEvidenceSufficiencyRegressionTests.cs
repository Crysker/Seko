using Seko.Infrastructure.Agent;

namespace Seko.Tests.Agent;

public sealed class AutonomyEvidenceSufficiencyRegressionTests
{
    [Fact]
    public void Router_ProjectExplanationRequiresWorkspaceEvidencePath()
    {
        var decision =
            SekoRequestRouter.Route(
                "Explain this project to me without changing anything.");

        Assert.False(
            decision.UseFastConversation);

        Assert.True(
            decision.TaskIntent.RequiresWorkspaceTools);

        Assert.False(
            decision.TaskIntent.RequiresModification);

        Assert.True(
            decision.TaskIntent.RequiresProjectExplanationEvidence);
    }

    [Fact]
    public void Router_OrdinaryExplanationDoesNotInventWorkspaceRequirement()
    {
        var decision =
            SekoRequestRouter.Route(
                "Explain the difference between RAM and storage.");

        Assert.True(
            decision.UseFastConversation);

        Assert.False(
            decision.TaskIntent.RequiresWorkspaceTools);

        Assert.False(
            decision.TaskIntent.RequiresProjectExplanationEvidence);
    }

    [Theory]
    [InlineData(
        "search_workspace",
        "No relevant accessible workspace matches were found for 'main functionality'. Scanned 4 searchable files.")]
    [InlineData(
        "find_files",
        "No accessible files matching 'Program.cs' were found.")]
    [InlineData(
        "find_text",
        "Text 'Main' was not found in Program.cs.")]
    public void ZeroMatchInspectionQueries_AreNoChange(
        string toolName,
        string result)
    {
        var controller =
            CreateProjectExplanationController();

        var state =
            Start(
                controller);

        var outcome =
            SekoAutonomyLiveLoop.ClassifyToolResult(
                state,
                toolName,
                result,
                toolSucceeded:
                    true,
                argumentsJson:
                    "{}");

        Assert.Equal(
            SekoAutonomyToolOutcomeKind.NoChange,
            outcome.Kind);

        Assert.Null(
            outcome.Signal);

        Assert.False(
            outcome.CountsAsMeaningfulProgress);
    }

    [Fact]
    public void ProjectExplanationGate_SearchEvidenceAloneCannotEnterSynthesis()
    {
        var controller =
            CreateProjectExplanationController();

        var state =
            Start(
                controller);

        state =
            controller.ApplyToolOutcome(
                state,
                SekoAutonomyToolOutcome.Success(
                    "search_workspace",
                    SekoAutonomySignal.WorkspaceEvidenceObserved,
                    """
                    WORKSPACE SEARCH: project purpose
                    SCANNED FILES: 4
                    RESULTS: 1

                    #1 SekoReadOnlyTest.csproj
                    """,
                    """{"query":"project purpose"}"""))
                .State;

        var decision =
            controller.ApplyModelResponseWithoutTools(
                state);

        Assert.Equal(
            SekoAutonomyDisposition.Continue,
            decision.Disposition);

        Assert.Equal(
            SekoAutonomyPhase.Inspection,
            decision.State.Phase);

        Assert.Equal(
            1,
            decision.State.ConsecutiveNoProgressRounds);

        Assert.Contains(
            "Project explanation evidence gate BLOCKED",
            decision.Reason,
            StringComparison.Ordinal);

        Assert.Contains(
            "list_files on the workspace root with recursive=true",
            decision.Reason,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectExplanationGate_RequiresRepresentativeDescriptorAndSourceCoverage()
    {
        var controller =
            CreateProjectExplanationController();

        var state =
            Start(
                controller);

        state =
            ApplyInventory(
                controller,
                state);

        state =
            ApplyRead(
                controller,
                state,
                "SekoReadOnlyTest.csproj");

        state =
            ApplyRead(
                controller,
                state,
                "Program.cs");

        var blocked =
            controller.ApplyModelResponseWithoutTools(
                state);

        Assert.Equal(
            SekoAutonomyPhase.Inspection,
            blocked.State.Phase);

        Assert.Contains(
            "inspected_relevant=2/3",
            blocked.Reason,
            StringComparison.Ordinal);

        Assert.Contains(
            "source=1/2",
            blocked.Reason,
            StringComparison.Ordinal);

        state =
            ApplyRead(
                controller,
                blocked.State,
                "Greeter.cs");

        var satisfied =
            controller.ApplyModelResponseWithoutTools(
                state);

        Assert.Equal(
            SekoAutonomyDisposition.Continue,
            satisfied.Disposition);

        Assert.Equal(
            SekoAutonomyPhase.Synthesis,
            satisfied.State.Phase);

        Assert.Contains(
            "Project explanation evidence gate SATISFIED",
            satisfied.Reason,
            StringComparison.Ordinal);

        Assert.Contains(
            "inspected_relevant=3/3",
            satisfied.Reason,
            StringComparison.Ordinal);

        Assert.Equal(
            3,
            satisfied.State.InspectedWorkspaceFiles.Count);
    }

    private static SekoAutonomyController CreateProjectExplanationController()
    {
        return new SekoAutonomyController(
            new SekoAutonomyTaskRequirements(
                RequiresResearch:
                    false,
                RequiresWorkspaceInspection:
                    true,
                RequiresModification:
                    false,
                RequiresVerification:
                    false)
            {
                RequiresProjectExplanationEvidence =
                    true
            });
    }

    private static SekoAutonomyState Start(
        SekoAutonomyController controller)
    {
        return
            controller.Start(
                controller.CreateInitialState())
                .State;
    }

    private static SekoAutonomyState ApplyInventory(
        SekoAutonomyController controller,
        SekoAutonomyState state)
    {
        return
            controller.ApplyToolOutcome(
                state,
                SekoAutonomyToolOutcome.Success(
                    "list_files",
                    SekoAutonomySignal.WorkspaceEvidenceObserved,
                    """
                    [FILE] Greeter.cs
                    [FILE] Program.cs
                    [FILE] README.md
                    [FILE] SekoReadOnlyTest.csproj
                    """,
                    """{"path":"","recursive":true}"""))
                .State;
    }

    private static SekoAutonomyState ApplyRead(
        SekoAutonomyController controller,
        SekoAutonomyState state,
        string path)
    {
        return
            controller.ApplyToolOutcome(
                state,
                SekoAutonomyToolOutcome.Success(
                    "read_file",
                    SekoAutonomySignal.WorkspaceEvidenceObserved,
                    $"FILE: {path}\nTOTAL LINES: 10\n\ncontent",
                    $"{{\"path\":\"{path}\"}}"))
                .State;
    }
}