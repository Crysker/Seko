using Seko.Infrastructure.Agent;

namespace Seko.Tests.Agent;

public sealed class AutonomyToolOutcomeRegressionTests
{
    [Fact]
    public void SuccessOutcome_ResetsNoProgressAndRecordsEvidence()
    {
        var controller =
            CreateInspectionController();

        var state =
            Start(
                controller);

        state =
            controller.ApplySignal(
                state,
                SekoAutonomySignal.NoProgress)
                .State;

        Assert.Equal(
            1,
            state.ConsecutiveNoProgressRounds);

        var decision =
            controller.ApplyToolOutcome(
                state,
                SekoAutonomyToolOutcome.Success(
                    "read_file",
                    SekoAutonomySignal.WorkspaceEvidenceObserved,
                    "file evidence"));

        Assert.Equal(
            SekoAutonomyDisposition.Continue,
            decision.Disposition);

        Assert.True(
            decision.State.WorkspaceEvidenceObserved);

        Assert.Equal(
            0,
            decision.State.ConsecutiveNoProgressRounds);
    }

    [Theory]
    [InlineData(SekoAutonomyToolOutcomeKind.Failure)]
    [InlineData(SekoAutonomyToolOutcomeKind.Blocked)]
    [InlineData(SekoAutonomyToolOutcomeKind.NoChange)]
    public void NonProgressOutcomes_DoNotResetExistingNoProgress(
        SekoAutonomyToolOutcomeKind kind)
    {
        var controller =
            CreateInspectionController();

        var state =
            Start(
                controller);

        state =
            controller.ApplySignal(
                state,
                SekoAutonomySignal.NoProgress)
                .State;

        var outcome =
            kind switch
            {
                SekoAutonomyToolOutcomeKind.Failure =>
                    SekoAutonomyToolOutcome.Failure(
                        "read_file",
                        detail:
                            "ERROR: simulated"),

                SekoAutonomyToolOutcomeKind.Blocked =>
                    SekoAutonomyToolOutcome.Blocked(
                        "write_file",
                        "blocked by phase policy"),

                SekoAutonomyToolOutcomeKind.NoChange =>
                    SekoAutonomyToolOutcome.NoChange(
                        "read_file",
                        "duplicate evidence"),

                _ =>
                    throw new InvalidOperationException(
                        $"Unexpected test outcome {kind}.")
            };

        var decision =
            controller.ApplyToolOutcome(
                state,
                outcome);

        Assert.Equal(
            SekoAutonomyDisposition.Continue,
            decision.Disposition);

        Assert.Equal(
            1,
            decision.State.ConsecutiveNoProgressRounds);

        var stalled =
            controller.ApplySignal(
                decision.State,
                SekoAutonomySignal.NoProgress);

        Assert.Equal(
            SekoAutonomyDisposition.Incomplete,
            stalled.Disposition);

        Assert.Equal(
            2,
            stalled.State.ConsecutiveNoProgressRounds);
    }

    [Fact]
    public void VerificationFailureOutcome_EntersRepair()
    {
        var controller =
            new SekoAutonomyController(
                new SekoAutonomyTaskRequirements(
                    RequiresResearch:
                        false,
                    RequiresWorkspaceInspection:
                        true,
                    RequiresModification:
                        true,
                    RequiresVerification:
                        true));

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

        var decision =
            controller.ApplyToolOutcome(
                state,
                SekoAutonomyToolOutcome.Failure(
                    "build_project",
                    SekoAutonomySignal.VerificationFailed,
                    "BUILD EXIT CODE: 1"));

        Assert.Equal(
            SekoAutonomyPhase.Repair,
            decision.State.Phase);

        Assert.Equal(
            1,
            decision.State.RepairCycles);
    }

    [Fact]
    public void NonSuccessOutcome_CannotClaimSuccessSignal()
    {
        var controller =
            CreateInspectionController();

        var state =
            Start(
                controller);

        var exception =
            Assert.Throws<InvalidOperationException>(
                () =>
                    controller.ApplyToolOutcome(
                        state,
                        SekoAutonomyToolOutcome.Failure(
                            "read_file",
                            SekoAutonomySignal.WorkspaceEvidenceObserved,
                            "ERROR")));

        Assert.Contains(
            "cannot report autonomy signal",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SuccessfulOutcome_CannotClaimVerificationFailure()
    {
        var controller =
            CreateInspectionController();

        var state =
            Start(
                controller);

        var exception =
            Assert.Throws<InvalidOperationException>(
                () =>
                    controller.ApplyToolOutcome(
                        state,
                        SekoAutonomyToolOutcome.Success(
                            "read_file",
                            SekoAutonomySignal.VerificationFailed,
                            "not a failure")));

        Assert.Contains(
            "cannot report autonomy signal",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Classifier_UsesExplicitFailureAndNoChangeStates()
    {
        var controller =
            new SekoAutonomyController(
                new SekoAutonomyTaskRequirements(
                    RequiresResearch:
                        false,
                    RequiresWorkspaceInspection:
                        true,
                    RequiresModification:
                        true,
                    RequiresVerification:
                        true));

        var state =
            Start(
                controller);

        state =
            controller.ApplySignal(
                state,
                SekoAutonomySignal.InspectionCompleted)
                .State;

        var failure =
            SekoAutonomyLiveLoop.ClassifyToolResult(
                state,
                "replace_text",
                "ERROR: OLD_TEXT_NOT_FOUND",
                toolSucceeded:
                    false);

        var noChange =
            SekoAutonomyLiveLoop.ClassifyToolResult(
                state,
                "replace_text",
                "No effective change required.",
                toolSucceeded:
                    true);

        Assert.Equal(
            SekoAutonomyToolOutcomeKind.Failure,
            failure.Kind);

        Assert.Equal(
            SekoAutonomyToolOutcomeKind.NoChange,
            noChange.Kind);

        Assert.False(
            failure.CountsAsMeaningfulProgress);

        Assert.False(
            noChange.CountsAsMeaningfulProgress);
    }

    private static SekoAutonomyController CreateInspectionController()
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
                    false));
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
