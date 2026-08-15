using Seko.Infrastructure.Agent;

namespace Seko.Tests.Agent;

public sealed class AutonomyLiveLoopRegressionTests
{
    [Fact]
    public void CreateController_MapsResearchWorkspaceModificationRequirements()
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
                    true);

        var started =
            controller.Start(
                controller.CreateInitialState());

        Assert.Equal(
            SekoAutonomyPhase.Research,
            started.State.Phase);
    }

    [Fact]
    public void ResearchSuccess_AdvancesToInspection()
    {
        var controller =
            CreateController(
                requiresWebResearch:
                    true,
                requiresWorkspaceTools:
                    true);

        var state =
            Start(
                controller);

        var decision =
            SekoAutonomyLiveLoop.ApplyToolResult(
                controller,
                state,
                "web_research",
                "evidence",
                toolSucceeded:
                    true);

        Assert.NotNull(
            decision);

        Assert.Equal(
            SekoAutonomyPhase.Inspection,
            decision!.State.Phase);

        Assert.True(
            decision.State.ResearchCompleted);
    }

    [Fact]
    public void Inspection_NoToolResponseAfterEvidence_AdvancesToAction()
    {
        var controller =
            CreateController(
                requiresWorkspaceTools:
                    true,
                requiresModification:
                    true);

        var state =
            Start(
                controller);

        Assert.Equal(
            SekoAutonomyPhase.Inspection,
            state.Phase);

        var decision =
            SekoAutonomyLiveLoop.ApplyNoToolResponse(
                controller,
                state,
                workspaceEvidenceObserved:
                    true);

        Assert.NotNull(
            decision);

        Assert.Equal(
            SekoAutonomyPhase.Action,
            decision!.State.Phase);

        Assert.True(
            decision.State.WorkspaceEvidenceObserved);
    }

    [Fact]
    public void InspectionWithoutEvidence_DoesNotAdvance()
    {
        var controller =
            CreateController(
                requiresWorkspaceTools:
                    true,
                requiresModification:
                    true);

        var state =
            Start(
                controller);

        var decision =
            SekoAutonomyLiveLoop.ApplyNoToolResponse(
                controller,
                state,
                workspaceEvidenceObserved:
                    false);

        Assert.Null(
            decision);
    }

    [Fact]
    public void SuccessfulModification_AdvancesDirectlyToVerification()
    {
        var controller =
            CreateController(
                requiresWorkspaceTools:
                    true,
                requiresModification:
                    true);

        var state =
            Start(
                controller);

        state =
            controller.ApplySignal(
                state,
                SekoAutonomySignal.InspectionCompleted)
                .State;

        var decision =
            SekoAutonomyLiveLoop.ApplyToolResult(
                controller,
                state,
                "replace_text",
                "Updated SomeFile.cs",
                toolSucceeded:
                    true);

        Assert.NotNull(
            decision);

        Assert.Equal(
            SekoAutonomyPhase.Verification,
            decision!.State.Phase);

        Assert.Equal(
            1,
            decision.State.ModificationGeneration);
    }

    [Fact]
    public void FailedVerification_EntersRepair_ThenRepairReturnsToVerification()
    {
        var controller =
            CreateController(
                requiresWorkspaceTools:
                    true,
                requiresModification:
                    true);

        var state =
            Start(
                controller);

        state =
            controller.ApplySignal(
                state,
                SekoAutonomySignal.InspectionCompleted)
                .State;

        state =
            controller.ApplySignal(
                state,
                SekoAutonomySignal.ModificationCompleted)
                .State;

        var failed =
            SekoAutonomyLiveLoop.ApplyToolResult(
                controller,
                state,
                "build_project",
                "BUILD EXIT CODE: 1`nCS1002: ; expected",
                toolSucceeded:
                    false);

        Assert.NotNull(
            failed);

        Assert.Equal(
            SekoAutonomyPhase.Repair,
            failed!.State.Phase);

        var repaired =
            SekoAutonomyLiveLoop.ApplyToolResult(
                controller,
                failed.State,
                "replace_text",
                "Updated SomeFile.cs",
                toolSucceeded:
                    true);

        Assert.NotNull(
            repaired);

        Assert.Equal(
            SekoAutonomyPhase.Verification,
            repaired!.State.Phase);

        Assert.Equal(
            2,
            repaired.State.ModificationGeneration);
    }

    [Fact]
    public void SuccessfulVerification_ThenSynthesis_CompletesTask()
    {
        var controller =
            CreateController(
                requiresWorkspaceTools:
                    true,
                requiresModification:
                    true);

        var state =
            Start(
                controller);

        state =
            controller.ApplySignal(
                state,
                SekoAutonomySignal.InspectionCompleted)
                .State;

        state =
            controller.ApplySignal(
                state,
                SekoAutonomySignal.ModificationCompleted)
                .State;

        var verified =
            SekoAutonomyLiveLoop.ApplyToolResult(
                controller,
                state,
                "build_project",
                "BUILD EXIT CODE: 0",
                toolSucceeded:
                    true);

        Assert.NotNull(
            verified);

        Assert.Equal(
            SekoAutonomyPhase.Synthesis,
            verified!.State.Phase);

        var completed =
            SekoAutonomyLiveLoop.ApplyNoToolResponse(
                controller,
                verified.State,
                workspaceEvidenceObserved:
                    true);

        Assert.NotNull(
            completed);

        Assert.Equal(
            SekoAutonomyDisposition.Complete,
            completed!.Disposition);

        Assert.Equal(
            SekoAutonomyPhase.Complete,
            completed.State.Phase);
    }

    [Fact]
    public void ReadOnlyInspection_AdvancesToSynthesisWhenModelStopsCallingTools()
    {
        var controller =
            CreateController(
                requiresWorkspaceTools:
                    true);

        var state =
            Start(
                controller);

        var decision =
            SekoAutonomyLiveLoop.ApplyNoToolResponse(
                controller,
                state,
                workspaceEvidenceObserved:
                    true);

        Assert.NotNull(
            decision);

        Assert.Equal(
            SekoAutonomyPhase.Synthesis,
            decision!.State.Phase);
    }

    [Fact]
    public void BuildOnlyTask_StartsInVerification()
    {
        var controller =
            CreateController(
                explicitBuildRequested:
                    true);

        var state =
            Start(
                controller);

        Assert.Equal(
            SekoAutonomyPhase.Verification,
            state.Phase);
    }

    [Fact]
    public void FailedNonVerificationTool_DoesNotInventPhaseProgress()
    {
        var controller =
            CreateController(
                requiresWorkspaceTools:
                    true,
                requiresModification:
                    true);

        var state =
            Start(
                controller);

        state =
            controller.ApplySignal(
                state,
                SekoAutonomySignal.InspectionCompleted)
                .State;

        var decision =
            SekoAutonomyLiveLoop.ApplyToolResult(
                controller,
                state,
                "replace_text",
                "ERROR: OLD_TEXT_NOT_FOUND",
                toolSucceeded:
                    false);

        Assert.Null(
            decision);

        Assert.Equal(
            SekoAutonomyPhase.Action,
            state.Phase);

        Assert.Equal(
            0,
            state.ModificationGeneration);
    }

    private static SekoAutonomyController CreateController(
        bool requiresWebResearch = false,
        bool requiresWorkspaceTools = false,
        bool requiresModification = false,
        bool explicitBuildRequested = false)
    {
        return
            SekoAutonomyLiveLoop.CreateController(
                new TaskIntent(
                    RequiresWorkspaceTools:
                        requiresWorkspaceTools,
                    RequiresModification:
                        requiresModification,
                    ExplicitBuildRequested:
                        explicitBuildRequested),
                requiresWebResearch);
    }

    private static SekoAutonomyState Start(
        SekoAutonomyController controller)
    {
        return
            controller.Start(
                controller.CreateInitialState())
                .State;
    }
}