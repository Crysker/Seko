namespace Seko.Infrastructure.Agent;

public sealed class SekoAutonomyController
{
    private readonly SekoAutonomyTaskRequirements _requirements;
    private readonly SekoAutonomyBudgetPolicy _budgetPolicy;

    public SekoAutonomyController(
        SekoAutonomyTaskRequirements requirements,
        SekoAutonomyBudgetPolicy? budgetPolicy = null)
    {
        _requirements =
            requirements
            ?? throw new ArgumentNullException(
                nameof(requirements));

        _budgetPolicy =
            budgetPolicy
            ?? SekoAutonomyBudgetPolicy.Default;
    }

    public SekoAutonomyState CreateInitialState()
    {
        return new SekoAutonomyState
        {
            WorkspaceModificationAllowed =
                _requirements.RequiresModification
        };
    }

    public SekoAutonomyDecision Start(
        SekoAutonomyState state)
    {
        ArgumentNullException.ThrowIfNull(
            state);

        EnsurePhase(
            state,
            SekoAutonomyPhase.Planning,
            nameof(Start));

        return Continue(
            Transition(
                state,
                SelectFirstExecutionPhase()),
            "Host planning selected the first required execution phase.");
    }

    public SekoAutonomyDecision BeginModelRound(
        SekoAutonomyState state)
    {
        ArgumentNullException.ThrowIfNull(
            state);

        if (state.IsTerminal)
        {
            return DecisionForTerminalState(
                state);
        }

        if (state.Phase
            == SekoAutonomyPhase.Planning)
        {
            throw new InvalidOperationException(
                "Start the autonomy controller before beginning a model round.");
        }

        if (state.TotalModelRounds
            >= _budgetPolicy.EmergencyGlobalRoundLimit)
        {
            return Incomplete(
                state,
                $"Emergency autonomy round ceiling reached ({_budgetPolicy.EmergencyGlobalRoundLimit}).");
        }

        var phaseBudget =
            _budgetPolicy.GetRoundBudget(
                state.Phase);

        if (state.PhaseModelRounds
            >= phaseBudget)
        {
            return Incomplete(
                state,
                $"Phase budget exhausted for {state.Phase} ({phaseBudget} model rounds).");
        }

        return Continue(
            state with
            {
                TotalModelRounds =
                    state.TotalModelRounds + 1,

                PhaseModelRounds =
                    state.PhaseModelRounds + 1
            },
            $"Model round started in {state.Phase}.");
    }

    public SekoAutonomyDecision ApplySignal(
        SekoAutonomyState state,
        SekoAutonomySignal signal,
        string? detail = null)
    {
        ArgumentNullException.ThrowIfNull(
            state);

        if (state.IsTerminal)
        {
            return DecisionForTerminalState(
                state);
        }

        return signal switch
        {
            SekoAutonomySignal.MeaningfulProgress =>
                RecordMeaningfulProgress(
                    state),

            SekoAutonomySignal.NoProgress =>
                RecordNoProgress(
                    state),

            SekoAutonomySignal.ResearchCompleted =>
                CompleteResearch(
                    state),

            SekoAutonomySignal.WorkspaceEvidenceObserved =>
                RecordWorkspaceEvidence(
                    state),

            SekoAutonomySignal.InspectionCompleted =>
                CompleteInspection(
                    state),

            SekoAutonomySignal.ModificationCompleted =>
                CompleteModification(
                    state),

            SekoAutonomySignal.VerificationSucceeded =>
                CompleteVerification(
                    state),

            SekoAutonomySignal.VerificationFailed =>
                FailVerification(
                    state,
                    detail),

            SekoAutonomySignal.RepairCompleted =>
                CompleteRepair(
                    state),

            SekoAutonomySignal.SynthesisCompleted =>
                CompleteSynthesis(
                    state),

            _ =>
                throw new ArgumentOutOfRangeException(
                    nameof(signal),
                    signal,
                    "Unsupported autonomy signal.")
        };
    }

    public SekoAutonomyDecision ApplyToolOutcome(
        SekoAutonomyState state,
        SekoAutonomyToolOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(
            state);

        ArgumentNullException.ThrowIfNull(
            outcome);

        if (state.IsTerminal)
        {
            return DecisionForTerminalState(
                state);
        }

        if (string.IsNullOrWhiteSpace(
                outcome.ToolName))
        {
            throw new ArgumentException(
                "A tool outcome must name the tool that produced it.",
                nameof(outcome));
        }

        if (outcome.Signal is
            SekoAutonomySignal signal)
        {
            ValidateToolOutcomeSignal(
                outcome,
                signal);

            return ApplySignal(
                state,
                signal,
                outcome.Detail);
        }

        return outcome.Kind switch
        {
            SekoAutonomyToolOutcomeKind.Success =>
                RecordMeaningfulProgress(
                    state),

            SekoAutonomyToolOutcomeKind.Failure =>
                Continue(
                    state,
                    $"Tool '{outcome.ToolName}' failed; failed tool execution does not count as meaningful progress."),

            SekoAutonomyToolOutcomeKind.Blocked =>
                Continue(
                    state,
                    $"Tool '{outcome.ToolName}' was blocked; blocked tool requests do not count as meaningful progress."),

            SekoAutonomyToolOutcomeKind.NoChange =>
                Continue(
                    state,
                    $"Tool '{outcome.ToolName}' produced no effective change; no-change outcomes do not count as meaningful progress."),

            _ =>
                throw new ArgumentOutOfRangeException(
                    nameof(outcome),
                    outcome.Kind,
                    "Unsupported autonomy tool outcome.")
        };
    }

    private static void ValidateToolOutcomeSignal(
        SekoAutonomyToolOutcome outcome,
        SekoAutonomySignal signal)
    {
        var valid =
            outcome.Kind
                == SekoAutonomyToolOutcomeKind.Success
            && signal is
                SekoAutonomySignal.ResearchCompleted
                or SekoAutonomySignal.WorkspaceEvidenceObserved
                or SekoAutonomySignal.ModificationCompleted
                or SekoAutonomySignal.VerificationSucceeded
                or SekoAutonomySignal.RepairCompleted

            || outcome.Kind
                == SekoAutonomyToolOutcomeKind.Failure
            && signal
                == SekoAutonomySignal.VerificationFailed;

        if (!valid)
        {
            throw new InvalidOperationException(
                $"Tool outcome {outcome.Kind} cannot report autonomy signal {signal}.");
        }
    }

    private SekoAutonomyDecision RecordMeaningfulProgress(
        SekoAutonomyState state)
    {
        return Continue(
            state with
            {
                ConsecutiveNoProgressRounds =
                    0
            },
            "Meaningful progress recorded.");
    }

    private SekoAutonomyDecision RecordNoProgress(
        SekoAutonomyState state)
    {
        var noProgressRounds =
            state.ConsecutiveNoProgressRounds + 1;

        var updated =
            state with
            {
                ConsecutiveNoProgressRounds =
                    noProgressRounds
            };

        if (noProgressRounds
            >= _budgetPolicy.MaximumConsecutiveNoProgressRounds)
        {
            return Incomplete(
                updated,
                $"No meaningful progress for {noProgressRounds} consecutive rounds in {state.Phase}.");
        }

        return Continue(
            updated,
            "No meaningful progress recorded; the controller permits one bounded strategy change.");
    }

    private SekoAutonomyDecision CompleteResearch(
        SekoAutonomyState state)
    {
        EnsurePhase(
            state,
            SekoAutonomyPhase.Research,
            nameof(SekoAutonomySignal.ResearchCompleted));

        var updated =
            state with
            {
                ResearchCompleted =
                    true
            };

        return Continue(
            Transition(
                updated,
                SelectPhaseAfterResearch()),
            "Research evidence accepted; advancing without returning to research.");
    }

    private SekoAutonomyDecision RecordWorkspaceEvidence(
        SekoAutonomyState state)
    {
        EnsurePhase(
            state,
            SekoAutonomyPhase.Inspection,
            nameof(SekoAutonomySignal.WorkspaceEvidenceObserved));

        return Continue(
            state with
            {
                WorkspaceEvidenceObserved =
                    true,

                ConsecutiveNoProgressRounds =
                    0
            },
            "Workspace evidence observed; inspection may continue until the model is ready to advance.");
    }

    private SekoAutonomyDecision CompleteInspection(
        SekoAutonomyState state)
    {
        EnsurePhase(
            state,
            SekoAutonomyPhase.Inspection,
            nameof(SekoAutonomySignal.InspectionCompleted));

        var updated =
            state with
            {
                WorkspaceEvidenceObserved =
                    true
            };

        return Continue(
            Transition(
                updated,
                SelectPhaseAfterInspection()),
            "Workspace evidence accepted; advancing to the next required phase.");
    }

    private SekoAutonomyDecision CompleteModification(
        SekoAutonomyState state)
    {
        EnsurePhase(
            state,
            SekoAutonomyPhase.Action,
            nameof(SekoAutonomySignal.ModificationCompleted));

        if (!_requirements.RequiresModification
            || !state.WorkspaceModificationAllowed)
        {
            return Incomplete(
                state,
                "Workspace modification was reported without original task permission.");
        }

        var updated =
            state with
            {
                ModificationGeneration =
                    state.ModificationGeneration + 1
            };

        return Continue(
            Transition(
                updated,
                SekoAutonomyPhase.Verification),
            "Modification recorded; verification is mandatory before completion.");
    }

    private SekoAutonomyDecision CompleteVerification(
        SekoAutonomyState state)
    {
        EnsurePhase(
            state,
            SekoAutonomyPhase.Verification,
            nameof(SekoAutonomySignal.VerificationSucceeded));

        if (_requirements.RequiresModification
            && state.ModificationGeneration == 0)
        {
            return Incomplete(
                state,
                "Verification cannot complete a modification task before a real modification is recorded.");
        }

        var updated =
            state with
            {
                VerifiedModificationGeneration =
                    state.ModificationGeneration,

                LastVerificationFailureSignature =
                    null,

                LastVerificationFailureGeneration =
                    -1
            };

        return Continue(
            Transition(
                updated,
                SekoAutonomyPhase.Synthesis),
            "Verification succeeded for the current modification generation.");
    }

    private SekoAutonomyDecision FailVerification(
        SekoAutonomyState state,
        string? detail)
    {
        EnsurePhase(
            state,
            SekoAutonomyPhase.Verification,
            nameof(SekoAutonomySignal.VerificationFailed));

        var signature =
            NormalizeFailureSignature(
                detail);

        var failed =
            state with
            {
                LastVerificationFailureSignature =
                    signature,

                LastVerificationFailureGeneration =
                    state.ModificationGeneration
            };

        if (!_requirements.RequiresModification
            || !state.WorkspaceModificationAllowed)
        {
            return Incomplete(
                failed,
                "Verification failed, but the original task did not grant workspace modification permission. Repair was not entered.");
        }

        if (state.RepairCycles
            >= _budgetPolicy.MaximumRepairCycles)
        {
            return Incomplete(
                failed,
                $"Verification still failed after {_budgetPolicy.MaximumRepairCycles} repair cycles.");
        }

        var updated =
            failed with
            {
                RepairCycles =
                    state.RepairCycles + 1
            };

        return Continue(
            Transition(
                updated,
                SekoAutonomyPhase.Repair),
            "Verification failed; entering bounded repair using verification evidence and the original task permissions.");
    }

    private SekoAutonomyDecision CompleteRepair(
        SekoAutonomyState state)
    {
        EnsurePhase(
            state,
            SekoAutonomyPhase.Repair,
            nameof(SekoAutonomySignal.RepairCompleted));

        if (!_requirements.RequiresModification
            || !state.WorkspaceModificationAllowed)
        {
            return Incomplete(
                state,
                "Repair cannot modify the workspace because the original task did not grant modification permission.");
        }

        var updated =
            state with
            {
                ModificationGeneration =
                    state.ModificationGeneration + 1
            };

        return Continue(
            Transition(
                updated,
                SekoAutonomyPhase.Verification),
            "Repair modification recorded; returning directly to verification.");
    }

    private SekoAutonomyDecision CompleteSynthesis(
        SekoAutonomyState state)
    {
        EnsurePhase(
            state,
            SekoAutonomyPhase.Synthesis,
            nameof(SekoAutonomySignal.SynthesisCompleted));

        var gateFailure =
            GetCompletionGateFailure(
                state);

        if (gateFailure is not null)
        {
            return Incomplete(
                state,
                gateFailure);
        }

        var completed =
            Transition(
                state,
                SekoAutonomyPhase.Complete);

        return new SekoAutonomyDecision(
            completed,
            SekoAutonomyDisposition.Complete,
            "All required autonomy completion gates are satisfied.");
    }

    private string? GetCompletionGateFailure(
        SekoAutonomyState state)
    {
        if (_requirements.RequiresResearch
            && !state.ResearchCompleted)
        {
            return "Completion blocked because required research evidence was not recorded.";
        }

        if (NeedsInspection()
            && !state.WorkspaceEvidenceObserved)
        {
            return "Completion blocked because required workspace evidence was not recorded.";
        }

        if (!_requirements.RequiresModification
            && state.ModificationGeneration > 0)
        {
            return "Completion blocked because workspace modifications occurred without original task permission.";
        }

        if (_requirements.RequiresModification
            && state.ModificationGeneration == 0)
        {
            return "Completion blocked because no successful modification was recorded.";
        }

        if (NeedsVerification()
            && state.VerifiedModificationGeneration
                < state.ModificationGeneration)
        {
            return "Completion blocked because the latest modification generation has not been verified.";
        }

        return null;
    }

    private SekoAutonomyPhase SelectFirstExecutionPhase()
    {
        if (_requirements.RequiresResearch)
        {
            return SekoAutonomyPhase.Research;
        }

        if (NeedsInspection())
        {
            return SekoAutonomyPhase.Inspection;
        }

        if (NeedsVerification())
        {
            return SekoAutonomyPhase.Verification;
        }

        return SekoAutonomyPhase.Synthesis;
    }

    private SekoAutonomyPhase SelectPhaseAfterResearch()
    {
        if (NeedsInspection())
        {
            return SekoAutonomyPhase.Inspection;
        }

        if (NeedsVerification())
        {
            return SekoAutonomyPhase.Verification;
        }

        return SekoAutonomyPhase.Synthesis;
    }

    private SekoAutonomyPhase SelectPhaseAfterInspection()
    {
        if (_requirements.RequiresModification)
        {
            return SekoAutonomyPhase.Action;
        }

        if (NeedsVerification())
        {
            return SekoAutonomyPhase.Verification;
        }

        return SekoAutonomyPhase.Synthesis;
    }

    private bool NeedsInspection()
    {
        return
            _requirements.RequiresWorkspaceInspection
            || _requirements.RequiresModification;
    }

    private bool NeedsVerification()
    {
        return
            _requirements.RequiresVerification
            || _requirements.RequiresModification;
    }

    private static SekoAutonomyState Transition(
        SekoAutonomyState state,
        SekoAutonomyPhase phase)
    {
        return state with
        {
            Phase =
                phase,

            PhaseModelRounds =
                0,

            ConsecutiveNoProgressRounds =
                0
        };
    }

    private static string NormalizeFailureSignature(
        string? detail)
    {
        if (string.IsNullOrWhiteSpace(
                detail))
        {
            return "verification-failed";
        }

        return detail
            .Trim()
            .Replace(
                "\r\n",
                "\n",
                StringComparison.Ordinal)
            .Replace(
                '\r',
                '\n');
    }

    private static void EnsurePhase(
        SekoAutonomyState state,
        SekoAutonomyPhase expected,
        string operation)
    {
        if (state.Phase != expected)
        {
            throw new InvalidOperationException(
                $"{operation} requires phase {expected}, but the controller is in {state.Phase}.");
        }
    }

    private static SekoAutonomyDecision Continue(
        SekoAutonomyState state,
        string reason)
    {
        return new SekoAutonomyDecision(
            state,
            SekoAutonomyDisposition.Continue,
            reason);
    }

    private static SekoAutonomyDecision Incomplete(
        SekoAutonomyState state,
        string reason)
    {
        var incomplete =
            state with
            {
                Phase =
                    SekoAutonomyPhase.Incomplete
            };

        return new SekoAutonomyDecision(
            incomplete,
            SekoAutonomyDisposition.Incomplete,
            reason);
    }

    private static SekoAutonomyDecision DecisionForTerminalState(
        SekoAutonomyState state)
    {
        return new SekoAutonomyDecision(
            state,
            state.Phase == SekoAutonomyPhase.Complete
                ? SekoAutonomyDisposition.Complete
                : SekoAutonomyDisposition.Incomplete,
            state.Phase == SekoAutonomyPhase.Complete
                ? "Autonomy task is already complete."
                : "Autonomy task is already incomplete.");
    }
}