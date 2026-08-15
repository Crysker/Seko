using Seko.Infrastructure.Agent;

namespace Seko.Tests.Agent;

public sealed class AutonomyControllerStateRegressionTests
{
    [Fact]
    public void Start_PureResearchBeginsInResearch()
    {
        var controller =
            CreateController(
                requiresResearch: true);

        var decision =
            controller.Start(
                controller.CreateInitialState());

        Assert.Equal(
            SekoAutonomyDisposition.Continue,
            decision.Disposition);

        Assert.Equal(
            SekoAutonomyPhase.Research,
            decision.State.Phase);
    }

    [Fact]
    public void Start_ModificationBeginsInInspection()
    {
        var controller =
            CreateController(
                requiresWorkspaceInspection: true,
                requiresModification: true);

        var decision =
            controller.Start(
                controller.CreateInitialState());

        Assert.Equal(
            SekoAutonomyPhase.Inspection,
            decision.State.Phase);
    }

    [Fact]
    public void Start_BuildOnlyTaskBeginsInVerification()
    {
        var controller =
            CreateController(
                requiresVerification: true);

        var decision =
            controller.Start(
                controller.CreateInitialState());

        Assert.Equal(
            SekoAutonomyPhase.Verification,
            decision.State.Phase);
    }

    [Fact]
    public void ResearchCompletion_AdvancesToInspectionAndCannotGoBackward()
    {
        var controller =
            CreateController(
                requiresResearch: true,
                requiresWorkspaceInspection: true);

        var started =
            controller.Start(
                controller.CreateInitialState());

        var researched =
            controller.ApplySignal(
                started.State,
                SekoAutonomySignal.ResearchCompleted);

        Assert.True(
            researched.State.ResearchCompleted);

        Assert.Equal(
            SekoAutonomyPhase.Inspection,
            researched.State.Phase);

        Assert.Throws<InvalidOperationException>(
            () =>
                controller.ApplySignal(
                    researched.State,
                    SekoAutonomySignal.ResearchCompleted));
    }

    [Fact]
    public void ReadOnlyInspection_AdvancesToSynthesis()
    {
        var controller =
            CreateController(
                requiresWorkspaceInspection: true);

        var started =
            controller.Start(
                controller.CreateInitialState());

        var inspected =
            controller.ApplySignal(
                started.State,
                SekoAutonomySignal.InspectionCompleted);

        Assert.True(
            inspected.State.WorkspaceEvidenceObserved);

        Assert.Equal(
            SekoAutonomyPhase.Synthesis,
            inspected.State.Phase);
    }

    [Fact]
    public void ModificationFlow_RequiresInspectionActionVerificationThenCompletes()
    {
        var controller =
            CreateController(
                requiresWorkspaceInspection: true,
                requiresModification: true);

        var state =
            controller.Start(
                controller.CreateInitialState())
                .State;

        state =
            controller.ApplySignal(
                state,
                SekoAutonomySignal.InspectionCompleted)
                .State;

        Assert.Equal(
            SekoAutonomyPhase.Action,
            state.Phase);

        state =
            controller.ApplySignal(
                state,
                SekoAutonomySignal.ModificationCompleted)
                .State;

        Assert.Equal(
            1,
            state.ModificationGeneration);

        Assert.Equal(
            SekoAutonomyPhase.Verification,
            state.Phase);

        state =
            controller.ApplySignal(
                state,
                SekoAutonomySignal.VerificationSucceeded)
                .State;

        Assert.Equal(
            1,
            state.VerifiedModificationGeneration);

        Assert.Equal(
            SekoAutonomyPhase.Synthesis,
            state.Phase);

        var completed =
            controller.ApplySignal(
                state,
                SekoAutonomySignal.SynthesisCompleted);

        Assert.Equal(
            SekoAutonomyDisposition.Complete,
            completed.Disposition);

        Assert.Equal(
            SekoAutonomyPhase.Complete,
            completed.State.Phase);
    }

    [Fact]
    public void VerificationFailure_EntersRepairAndRepairReturnsDirectlyToVerification()
    {
        var controller =
            CreateController(
                requiresWorkspaceInspection: true,
                requiresModification: true);

        var state =
            AdvanceModificationToVerification(
                controller);

        var failed =
            controller.ApplySignal(
                state,
                SekoAutonomySignal.VerificationFailed,
                "CS1002: ; expected");

        Assert.Equal(
            SekoAutonomyPhase.Repair,
            failed.State.Phase);

        Assert.Equal(
            1,
            failed.State.RepairCycles);

        Assert.Equal(
            "CS1002: ; expected",
            failed.State.LastVerificationFailureSignature);

        var repaired =
            controller.ApplySignal(
                failed.State,
                SekoAutonomySignal.RepairCompleted);

        Assert.Equal(
            SekoAutonomyPhase.Verification,
            repaired.State.Phase);

        Assert.Equal(
            2,
            repaired.State.ModificationGeneration);
    }

    [Fact]
    public void RepairCannotRunWithoutFailedVerification()
    {
        var controller =
            CreateController(
                requiresWorkspaceInspection: true,
                requiresModification: true);

        var state =
            controller.Start(
                controller.CreateInitialState())
                .State;

        state =
            controller.ApplySignal(
                state,
                SekoAutonomySignal.InspectionCompleted)
                .State;

        Assert.Equal(
            SekoAutonomyPhase.Action,
            state.Phase);

        Assert.Throws<InvalidOperationException>(
            () =>
                controller.ApplySignal(
                    state,
                    SekoAutonomySignal.RepairCompleted));
    }

    [Fact]
    public void RepairCycleLimit_StopsAfterSecondRepairFailsVerification()
    {
        var controller =
            CreateController(
                requiresWorkspaceInspection: true,
                requiresModification: true);

        var state =
            AdvanceModificationToVerification(
                controller);

        state =
            controller.ApplySignal(
                state,
                SekoAutonomySignal.VerificationFailed,
                "failure-one")
                .State;

        state =
            controller.ApplySignal(
                state,
                SekoAutonomySignal.RepairCompleted)
                .State;

        state =
            controller.ApplySignal(
                state,
                SekoAutonomySignal.VerificationFailed,
                "failure-two")
                .State;

        state =
            controller.ApplySignal(
                state,
                SekoAutonomySignal.RepairCompleted)
                .State;

        var exhausted =
            controller.ApplySignal(
                state,
                SekoAutonomySignal.VerificationFailed,
                "failure-three");

        Assert.Equal(
            SekoAutonomyDisposition.Incomplete,
            exhausted.Disposition);

        Assert.Equal(
            SekoAutonomyPhase.Incomplete,
            exhausted.State.Phase);

        Assert.Contains(
            "2 repair cycles",
            exhausted.Reason);
    }

    [Fact]
    public void PhaseBudget_StopsBeforeEmergencyCeiling()
    {
        var policy =
            CreatePolicy(
                inspectionRounds: 2,
                emergencyGlobalRoundLimit: 32);

        var controller =
            CreateController(
                requiresWorkspaceInspection: true,
                policy: policy);

        var state =
            controller.Start(
                controller.CreateInitialState())
                .State;

        state =
            controller.BeginModelRound(
                state)
                .State;

        state =
            controller.BeginModelRound(
                state)
                .State;

        var exhausted =
            controller.BeginModelRound(
                state);

        Assert.Equal(
            SekoAutonomyDisposition.Incomplete,
            exhausted.Disposition);

        Assert.Equal(
            2,
            exhausted.State.TotalModelRounds);

        Assert.Contains(
            "Phase budget exhausted",
            exhausted.Reason);
    }

    [Fact]
    public void EmergencyGlobalBudget_RemainsHardFailsafe()
    {
        var policy =
            CreatePolicy(
                researchRounds: 10,
                emergencyGlobalRoundLimit: 3);

        var controller =
            CreateController(
                requiresResearch: true,
                policy: policy);

        var state =
            controller.Start(
                controller.CreateInitialState())
                .State;

        for (var index = 0;
             index < 3;
             index++)
        {
            state =
                controller.BeginModelRound(
                    state)
                    .State;
        }

        var exhausted =
            controller.BeginModelRound(
                state);

        Assert.Equal(
            SekoAutonomyDisposition.Incomplete,
            exhausted.Disposition);

        Assert.Equal(
            3,
            exhausted.State.TotalModelRounds);

        Assert.Contains(
            "Emergency autonomy round ceiling",
            exhausted.Reason);
    }

    [Fact]
    public void ConsecutiveNoProgress_StopsAtPolicyLimit()
    {
        var controller =
            CreateController(
                requiresWorkspaceInspection: true);

        var state =
            controller.Start(
                controller.CreateInitialState())
                .State;

        var first =
            controller.ApplySignal(
                state,
                SekoAutonomySignal.NoProgress);

        Assert.Equal(
            SekoAutonomyDisposition.Continue,
            first.Disposition);

        var second =
            controller.ApplySignal(
                first.State,
                SekoAutonomySignal.NoProgress);

        Assert.Equal(
            SekoAutonomyDisposition.Incomplete,
            second.Disposition);

        Assert.Equal(
            2,
            second.State.ConsecutiveNoProgressRounds);
    }

    [Fact]
    public void MeaningfulProgress_ResetsNoProgressCounter()
    {
        var controller =
            CreateController(
                requiresWorkspaceInspection: true);

        var state =
            controller.Start(
                controller.CreateInitialState())
                .State;

        state =
            controller.ApplySignal(
                state,
                SekoAutonomySignal.NoProgress)
                .State;

        Assert.Equal(
            1,
            state.ConsecutiveNoProgressRounds);

        state =
            controller.ApplySignal(
                state,
                SekoAutonomySignal.MeaningfulProgress)
                .State;

        Assert.Equal(
            0,
            state.ConsecutiveNoProgressRounds);

        var nextMiss =
            controller.ApplySignal(
                state,
                SekoAutonomySignal.NoProgress);

        Assert.Equal(
            SekoAutonomyDisposition.Continue,
            nextMiss.Disposition);

        Assert.Equal(
            1,
            nextMiss.State.ConsecutiveNoProgressRounds);
    }

    [Fact]
    public void PhaseTransition_ResetsPerPhaseRoundAndNoProgressCounters()
    {
        var controller =
            CreateController(
                requiresResearch: true,
                requiresWorkspaceInspection: true);

        var state =
            controller.Start(
                controller.CreateInitialState())
                .State;

        state =
            controller.BeginModelRound(
                state)
                .State;

        state =
            controller.ApplySignal(
                state,
                SekoAutonomySignal.NoProgress)
                .State;

        var researched =
            controller.ApplySignal(
                state,
                SekoAutonomySignal.ResearchCompleted);

        Assert.Equal(
            SekoAutonomyPhase.Inspection,
            researched.State.Phase);

        Assert.Equal(
            0,
            researched.State.PhaseModelRounds);

        Assert.Equal(
            0,
            researched.State.ConsecutiveNoProgressRounds);

        Assert.Equal(
            1,
            researched.State.TotalModelRounds);
    }

    [Fact]
    public void CompletionGate_RejectsSyntheticUnverifiedModificationState()
    {
        var controller =
            CreateController(
                requiresWorkspaceInspection: true,
                requiresModification: true);

        var syntheticState =
            new SekoAutonomyState
            {
                Phase =
                    SekoAutonomyPhase.Synthesis,

                WorkspaceEvidenceObserved =
                    true,

                ModificationGeneration =
                    2,

                VerifiedModificationGeneration =
                    1
            };

        var decision =
            controller.ApplySignal(
                syntheticState,
                SekoAutonomySignal.SynthesisCompleted);

        Assert.Equal(
            SekoAutonomyDisposition.Incomplete,
            decision.Disposition);

        Assert.Contains(
            "latest modification generation",
            decision.Reason);
    }

    [Fact]
    public void VerificationCannotCompleteModificationTaskBeforeModificationExists()
    {
        var controller =
            CreateController(
                requiresModification: true);

        var syntheticState =
            new SekoAutonomyState
            {
                Phase =
                    SekoAutonomyPhase.Verification,

                WorkspaceEvidenceObserved =
                    true
            };

        var decision =
            controller.ApplySignal(
                syntheticState,
                SekoAutonomySignal.VerificationSucceeded);

        Assert.Equal(
            SekoAutonomyDisposition.Incomplete,
            decision.Disposition);

        Assert.Contains(
            "before a real modification",
            decision.Reason);
    }

    private static SekoAutonomyState AdvanceModificationToVerification(
        SekoAutonomyController controller)
    {
        var state =
            controller.Start(
                controller.CreateInitialState())
                .State;

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
        bool requiresResearch = false,
        bool requiresWorkspaceInspection = false,
        bool requiresModification = false,
        bool requiresVerification = false,
        SekoAutonomyBudgetPolicy? policy = null)
    {
        return new SekoAutonomyController(
            new SekoAutonomyTaskRequirements(
                requiresResearch,
                requiresWorkspaceInspection,
                requiresModification,
                requiresVerification),
            policy);
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