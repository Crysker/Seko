using Seko.Infrastructure.Agent;

namespace Seko.Tests.Agent;

public sealed class AutonomyAuthorityRegressionTests
{
    [Fact]
    public void StalledInspection_UsesControllerNoProgressLimit()
    {
        var controller =
            CreateController(
                requiresWorkspaceTools:
                    true);

        var state =
            Start(
                controller);

        var first =
            SekoAutonomyLiveLoop.ApplyModelResponseWithoutTools(
                controller,
                state);

        Assert.Equal(
            SekoAutonomyDisposition.Continue,
            first.Disposition);

        Assert.Equal(
            1,
            first.State.ConsecutiveNoProgressRounds);

        var second =
            SekoAutonomyLiveLoop.ApplyModelResponseWithoutTools(
                controller,
                first.State);

        Assert.Equal(
            SekoAutonomyDisposition.Incomplete,
            second.Disposition);

        Assert.Equal(
            SekoAutonomyPhase.Incomplete,
            second.State.Phase);

        Assert.Equal(
            2,
            second.State.ConsecutiveNoProgressRounds);
    }

    [Fact]
    public void MeaningfulInspectionEvidence_ResetsControllerNoProgressCounter()
    {
        var controller =
            CreateController(
                requiresWorkspaceTools:
                    true);

        var state =
            Start(
                controller);

        state =
            SekoAutonomyLiveLoop.ApplyModelResponseWithoutTools(
                controller,
                state)
            .State;

        Assert.Equal(
            1,
            state.ConsecutiveNoProgressRounds);

        var progress =
            SekoAutonomyLiveLoop.ApplyToolResult(
                controller,
                state,
                "read_file",
                "file contents",
                toolSucceeded:
                    true);

        Assert.NotNull(
            progress);

        Assert.Equal(
            SekoAutonomyPhase.Inspection,
            progress!.State.Phase);

        Assert.True(
            progress.State.WorkspaceEvidenceObserved);

        Assert.Equal(
            0,
            progress.State.ConsecutiveNoProgressRounds);
    }

    [Fact]
    public void PhaseBudget_IsEnforcedByControllerUsedByLiveLoop()
    {
        var policy =
            CreatePolicy(
                inspectionRounds:
                    1,
                maximumConsecutiveNoProgressRounds:
                    5);

        var controller =
            CreateController(
                requiresWorkspaceTools:
                    true,
                policy:
                    policy);

        var state =
            Start(
                controller);

        var first =
            controller.BeginModelRound(
                state);

        Assert.Equal(
            SekoAutonomyDisposition.Continue,
            first.Disposition);

        var exhausted =
            controller.BeginModelRound(
                first.State);

        Assert.Equal(
            SekoAutonomyDisposition.Incomplete,
            exhausted.Disposition);

        Assert.Contains(
            "Phase budget exhausted",
            exhausted.Reason);
    }

    [Fact]
    public void BuildOnlyFailure_DoesNotEnterRepairOrGainWritePermission()
    {
        var controller =
            CreateController(
                explicitBuildRequested:
                    true);

        var state =
            Start(
                controller);

        Assert.False(
            state.WorkspaceModificationAllowed);

        var failed =
            SekoAutonomyLiveLoop.ApplyToolResult(
                controller,
                state,
                "build_project",
                "BUILD EXIT CODE: 1",
                toolSucceeded:
                    false);

        Assert.NotNull(
            failed);

        Assert.Equal(
            SekoAutonomyDisposition.Incomplete,
            failed!.Disposition);

        Assert.Equal(
            SekoAutonomyPhase.Incomplete,
            failed.State.Phase);

        Assert.Equal(
            0,
            failed.State.RepairCycles);

        Assert.Contains(
            "did not grant workspace modification permission",
            failed.Reason);

        var plan =
            SekoAutonomyToolPlanner.Create(
                failed.State);

        Assert.False(
            plan.Allows(
                "write_file"));

        Assert.False(
            plan.Allows(
                "replace_text"));
    }

    [Fact]
    public void SyntheticRepairWithoutOriginalPermission_FailsClosed()
    {
        var state =
            new SekoAutonomyState
            {
                Phase =
                    SekoAutonomyPhase.Repair,

                WorkspaceModificationAllowed =
                    false
            };

        var plan =
            SekoAutonomyToolPlanner.Create(
                state);

        Assert.Empty(
            plan.ToolNames);

        Assert.False(
            plan.Allows(
                "write_file"));

        Assert.Contains(
            "did not grant workspace modification permission",
            plan.Reason);
    }

    [Fact]
    public void ModificationRepair_PreservesOriginalWritePermission()
    {
        var controller =
            CreateController(
                requiresWorkspaceTools:
                    true,
                requiresModification:
                    true);

        var state =
            AdvanceModificationToVerification(
                controller);

        var failed =
            SekoAutonomyLiveLoop.ApplyToolResult(
                controller,
                state,
                "build_project",
                "BUILD EXIT CODE: 1",
                toolSucceeded:
                    false);

        Assert.NotNull(
            failed);

        Assert.Equal(
            SekoAutonomyPhase.Repair,
            failed!.State.Phase);

        Assert.True(
            failed.State.WorkspaceModificationAllowed);

        var repairPlan =
            SekoAutonomyToolPlanner.Create(
                failed.State);

        Assert.True(
            repairPlan.Allows(
                "write_file"));

        Assert.True(
            repairPlan.Allows(
                "replace_text"));
    }

    [Fact]
    public void RepairLimit_IsOwnedByControllerAcrossLiveToolResults()
    {
        var controller =
            CreateController(
                requiresWorkspaceTools:
                    true,
                requiresModification:
                    true);

        var state =
            AdvanceModificationToVerification(
                controller);

        state =
            FailVerification(
                controller,
                state,
                "failure-one")
            .State;

        state =
            CompleteRepair(
                controller,
                state)
            .State;

        state =
            FailVerification(
                controller,
                state,
                "failure-two")
            .State;

        state =
            CompleteRepair(
                controller,
                state)
            .State;

        var exhausted =
            FailVerification(
                controller,
                state,
                "failure-three");

        Assert.Equal(
            SekoAutonomyDisposition.Incomplete,
            exhausted.Disposition);

        Assert.Equal(
            SekoAutonomyPhase.Incomplete,
            exhausted.State.Phase);

        Assert.Equal(
            2,
            exhausted.State.RepairCycles);

        Assert.Contains(
            "2 repair cycles",
            exhausted.Reason);
    }

    [Fact]
    public void PermissionSurvivesEveryModificationPhaseTransition()
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

        Assert.True(
            state.WorkspaceModificationAllowed);

        state =
            controller.ApplySignal(
                state,
                SekoAutonomySignal.InspectionCompleted)
            .State;

        Assert.Equal(
            SekoAutonomyPhase.Action,
            state.Phase);

        Assert.True(
            state.WorkspaceModificationAllowed);

        state =
            controller.ApplySignal(
                state,
                SekoAutonomySignal.ModificationCompleted)
            .State;

        Assert.Equal(
            SekoAutonomyPhase.Verification,
            state.Phase);

        Assert.True(
            state.WorkspaceModificationAllowed);

        state =
            controller.ApplySignal(
                state,
                SekoAutonomySignal.VerificationFailed,
                "failure")
            .State;

        Assert.Equal(
            SekoAutonomyPhase.Repair,
            state.Phase);

        Assert.True(
            state.WorkspaceModificationAllowed);
    }

    private static SekoAutonomyDecision FailVerification(
        SekoAutonomyController controller,
        SekoAutonomyState state,
        string failure)
    {
        return
            SekoAutonomyLiveLoop.ApplyToolResult(
                controller,
                state,
                "build_project",
                failure,
                toolSucceeded:
                    false)
            ?? throw new InvalidOperationException(
                "Expected verification failure decision.");
    }

    private static SekoAutonomyDecision CompleteRepair(
        SekoAutonomyController controller,
        SekoAutonomyState state)
    {
        return
            SekoAutonomyLiveLoop.ApplyToolResult(
                controller,
                state,
                "replace_text",
                "Updated SomeFile.cs",
                toolSucceeded:
                    true)
            ?? throw new InvalidOperationException(
                "Expected repair completion decision.");
    }

    private static SekoAutonomyState AdvanceModificationToVerification(
        SekoAutonomyController controller)
    {
        var state =
            Start(
                controller);

        state =
            controller.ApplySignal(
                state,
                SekoAutonomySignal.InspectionCompleted)
            .State;

        return controller.ApplySignal(
            state,
            SekoAutonomySignal.ModificationCompleted)
        .State;
    }

    private static SekoAutonomyController CreateController(
        bool requiresWebResearch = false,
        bool requiresWorkspaceTools = false,
        bool requiresModification = false,
        bool explicitBuildRequested = false,
        SekoAutonomyBudgetPolicy? policy = null)
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
                requiresWebResearch,
                policy);
    }

    private static SekoAutonomyState Start(
        SekoAutonomyController controller)
    {
        return
            controller.Start(
                controller.CreateInitialState())
            .State;
    }

    private static SekoAutonomyBudgetPolicy CreatePolicy(
        int researchRounds = 2,
        int inspectionRounds = 6,
        int actionRounds = 6,
        int verificationRounds = 3,
        int repairRounds = 4,
        int synthesisRounds = 1,
        int maximumRepairCycles = 2,
        int maximumConsecutiveNoProgressRounds = 2,
        int emergencyGlobalRoundLimit = 32)
    {
        return new SekoAutonomyBudgetPolicy(
            researchRounds,
            inspectionRounds,
            actionRounds,
            verificationRounds,
            repairRounds,
            synthesisRounds,
            maximumRepairCycles,
            maximumConsecutiveNoProgressRounds,
            emergencyGlobalRoundLimit);
    }
}