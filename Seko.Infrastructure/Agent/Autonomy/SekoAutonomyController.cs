using System.Text.Json;

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
                _requirements.RequiresModification,

            ProjectExplanationEvidenceRequired =
                _requirements.RequiresProjectExplanationEvidence
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

    public SekoAutonomyDecision ApplyModelResponseWithoutTools(
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
            == SekoAutonomyPhase.Inspection)
        {
            if (_requirements.RequiresProjectExplanationEvidence)
            {
                return CompleteInspection(
                    state);
            }

            if (state.WorkspaceEvidenceObserved)
            {
                return CompleteInspection(
                    state);
            }

            return RecordNoProgress(
                state);
        }

        if (state.Phase
            == SekoAutonomyPhase.Synthesis)
        {
            return CompleteSynthesis(
                state);
        }

        return RecordNoProgress(
            state);
    }

    public string BuildProjectExplanationEvidenceRecoveryInstruction(
        SekoAutonomyState state)
    {
        ArgumentNullException.ThrowIfNull(
            state);

        if (!_requirements.RequiresProjectExplanationEvidence)
        {
            throw new InvalidOperationException(
                "Project explanation recovery was requested for a task that does not require the project evidence gate.");
        }

        var gate =
            EvaluateProjectExplanationEvidence(
                state);

        if (gate.Satisfied)
        {
            return
                "PROJECT EXPLANATION EVIDENCE RECOVERY\n\n" +
                "The evidence gate is already satisfied. Proceed to synthesis without more inspection.";
        }

        if (!state.ProjectInventoryObserved)
        {
            return
                $"""
                PROJECT EXPLANATION EVIDENCE RECOVERY

                The project explanation evidence gate is BLOCKED.

                {gate.Reason}

                REQUIRED NEXT ACTION:
                Run list_files on the workspace root with recursive=true.
                Do not answer the user yet.
                Do not attempt synthesis until the controller reports that the evidence gate is satisfied.
                """;
        }

        var candidates =
            gate.RequiredInspectionCandidates;

        var candidateLines =
            candidates.Count == 0
                ? "- No concrete candidate path is available yet; use the gate reason to gather one materially new piece of read-only evidence."
                : string.Join(
                    Environment.NewLine,
                    candidates.Select(
                        path => "- " + path));

        return
            $"""
            PROJECT EXPLANATION EVIDENCE RECOVERY

            The project explanation evidence gate is BLOCKED.

            {gate.Reason}

            REQUIRED FILE INSPECTION:
            {candidateLines}

            REQUIRED NEXT ACTION:
            Use read_file on every concrete path listed above before attempting synthesis again.
            Do not answer the user yet.
            Do not repeat broad searches when the controller already names concrete candidate paths.
            The existing bounded no-progress policy remains authoritative.
            """;
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

            if (signal
                == SekoAutonomySignal.WorkspaceEvidenceObserved)
            {
                return RecordWorkspaceEvidence(
                    state,
                    outcome);
            }

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
        SekoAutonomyState state,
        string? reason = null)
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
            var stallReason =
                $"No meaningful progress for {noProgressRounds} consecutive rounds in {state.Phase}.";

            return Incomplete(
                updated,
                string.IsNullOrWhiteSpace(
                    reason)
                    ? stallReason
                    : reason + " " + stallReason);
        }

        return Continue(
            updated,
            string.IsNullOrWhiteSpace(
                reason)
                ? "No meaningful progress recorded; the controller permits one bounded strategy change."
                : reason);
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
        SekoAutonomyState state,
        SekoAutonomyToolOutcome? outcome = null)
    {
        EnsurePhase(
            state,
            SekoAutonomyPhase.Inspection,
            nameof(SekoAutonomySignal.WorkspaceEvidenceObserved));

        var updated =
            state with
            {
                WorkspaceEvidenceObserved =
                    true,

                ConsecutiveNoProgressRounds =
                    0
            };

        if (outcome is null)
        {
            return Continue(
                updated,
                "Workspace evidence observed; inspection may continue until the model is ready to advance.");
        }

        updated =
            ApplyProjectEvidenceObservation(
                updated,
                outcome);

        if (_requirements.RequiresProjectExplanationEvidence)
        {
            var gate =
                EvaluateProjectExplanationEvidence(
                    updated);

            updated =
                updated with
                {
                    ProjectExplanationRecoveryCandidates =
                        gate.RequiredInspectionCandidates
                };

            if (gate.Satisfied)
            {
                var satisfied =
                    updated with
                    {
                        ProjectExplanationRecoveryCandidates =
                            Array.Empty<string>()
                    };

                return Continue(
                    Transition(
                        satisfied,
                        SelectPhaseAfterInspection()),
                    gate.Reason);
            }

            return Continue(
                updated,
                gate.Reason);
        }

        return Continue(
            updated,
            $"Workspace evidence observed from '{outcome.ToolName}'; inspection may continue until the model is ready to advance.");
    }

    private SekoAutonomyDecision CompleteInspection(
        SekoAutonomyState state)
    {
        EnsurePhase(
            state,
            SekoAutonomyPhase.Inspection,
            nameof(SekoAutonomySignal.InspectionCompleted));

        if (_requirements.RequiresProjectExplanationEvidence)
        {
            var gate =
                EvaluateProjectExplanationEvidence(
                    state);

            var gatedState =
                state with
                {
                    ProjectExplanationRecoveryCandidates =
                        gate.RequiredInspectionCandidates
                };

            if (!gate.Satisfied)
            {
                return RecordNoProgress(
                    gatedState,
                    gate.Reason);
            }

            var projectUpdated =
                gatedState with
                {
                    WorkspaceEvidenceObserved =
                        true,

                    ProjectExplanationRecoveryCandidates =
                        Array.Empty<string>()
                };

            return Continue(
                Transition(
                    projectUpdated,
                    SelectPhaseAfterInspection()),
                gate.Reason);
        }

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

        if (_requirements.RequiresProjectExplanationEvidence)
        {
            var projectEvidenceGate =
                EvaluateProjectExplanationEvidence(
                    state);

            if (!projectEvidenceGate.Satisfied)
            {
                return
                    "Completion blocked because " +
                    projectEvidenceGate.Reason;
            }
        }
        else if (NeedsInspection()
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

    private static SekoAutonomyState ApplyProjectEvidenceObservation(
        SekoAutonomyState state,
        SekoAutonomyToolOutcome outcome)
    {
        var updated =
            state;

        if (outcome.ToolName.Equals(
                "list_files",
                StringComparison.Ordinal)
            && IsRecursiveRootInventory(
                outcome.ArgumentsJson))
        {
            var inventory =
                ParseProjectInventory(
                    outcome.Detail);

            updated =
                updated with
                {
                    ProjectInventoryObserved =
                        true,

                    ProjectInventoryDirectoryCount =
                        inventory.DirectoryCount,

                    ProjectInventoryFiles =
                        inventory.Files
                };
        }

        if (outcome.ToolName is
            "read_file"
            or "find_text")
        {
            var inspectedPath =
                GetStringArgument(
                    outcome.ArgumentsJson,
                    "path")
                ?? ExtractFilePath(
                    outcome.Detail);

            if (!string.IsNullOrWhiteSpace(
                    inspectedPath))
            {
                updated =
                    updated with
                    {
                        InspectedWorkspaceFiles =
                            AddDistinctPath(
                                updated.InspectedWorkspaceFiles,
                                inspectedPath)
                    };
            }
        }

        return updated;
    }

    private static SekoProjectExplanationEvidenceGate
        EvaluateProjectExplanationEvidence(
            SekoAutonomyState state)
    {
        if (!state.ProjectInventoryObserved)
        {
            return new SekoProjectExplanationEvidenceGate(
                false,
                "Project explanation evidence gate BLOCKED: inventory=False; " +
                $"inspected_files={state.InspectedWorkspaceFiles.Count}. " +
                "Missing: run list_files on the workspace root with recursive=true before synthesis.",
                Array.Empty<string>());
        }

        var relevantInventoryFiles =
            state.ProjectInventoryFiles
                .Where(
                    IsProjectExplanationRelevantFile)
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        if (relevantInventoryFiles.Length == 0)
        {
            return new SekoProjectExplanationEvidenceGate(
                true,
                "Project explanation evidence gate SATISFIED: inventory=True; " +
                $"inventory_files={state.ProjectInventoryFiles.Count}; " +
                $"inventory_dirs={state.ProjectInventoryDirectoryCount}; " +
                "relevant_files=0; inspected_relevant=0/0; " +
                "descriptor=not-required; source=not-required.",
                Array.Empty<string>());
        }

        var relevantSet =
            new HashSet<string>(
                relevantInventoryFiles,
                StringComparer.OrdinalIgnoreCase);

        var inspectedRelevantFiles =
            state.InspectedWorkspaceFiles
                .Where(
                    relevantSet.Contains)
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        var requiredFileCount =
            Math.Min(
                3,
                relevantInventoryFiles.Length);

        var descriptorRequired =
            relevantInventoryFiles.Any(
                IsProjectDescriptor);

        var sourceFileRequirement =
            Math.Min(
                2,
                relevantInventoryFiles.Count(
                    IsSourceFile));

        var descriptorInspected =
            inspectedRelevantFiles.Any(
                IsProjectDescriptor);

        var inspectedSourceFileCount =
            inspectedRelevantFiles.Count(
                IsSourceFile);

        var missing =
            new List<string>();

        if (inspectedRelevantFiles.Length
            < requiredFileCount)
        {
            missing.Add(
                $"inspect {requiredFileCount - inspectedRelevantFiles.Length} more relevant file(s)");
        }

        if (descriptorRequired
            && !descriptorInspected)
        {
            missing.Add(
                "inspect a project/build descriptor");
        }

        if (inspectedSourceFileCount
            < sourceFileRequirement)
        {
            missing.Add(
                $"inspect {sourceFileRequirement - inspectedSourceFileCount} more source/entry-point file(s)");
        }

        var requiredInspectionCandidates =
            BuildRequiredInspectionCandidates(
                relevantInventoryFiles,
                inspectedRelevantFiles,
                descriptorRequired,
                descriptorInspected,
                sourceFileRequirement,
                inspectedSourceFileCount,
                requiredFileCount);

        var summary =
            "inventory=True; " +
            $"inventory_files={state.ProjectInventoryFiles.Count}; " +
            $"inventory_dirs={state.ProjectInventoryDirectoryCount}; " +
            $"relevant_files={relevantInventoryFiles.Length}; " +
            $"inspected_relevant={inspectedRelevantFiles.Length}/{requiredFileCount}; " +
            $"descriptor={(descriptorRequired ? descriptorInspected ? "inspected" : "missing" : "not-required")}; " +
            $"source={inspectedSourceFileCount}/{sourceFileRequirement}";

        if (missing.Count > 0)
        {
            var candidateSummary =
                requiredInspectionCandidates.Count == 0
                    ? "none"
                    : string.Join(
                        "; ",
                        requiredInspectionCandidates);

            return new SekoProjectExplanationEvidenceGate(
                false,
                "Project explanation evidence gate BLOCKED: " +
                summary +
                ". Missing: " +
                string.Join(
                    "; ",
                    missing) +
                ". Required inspection candidates: " +
                candidateSummary +
                ".",
                requiredInspectionCandidates);
        }

        return new SekoProjectExplanationEvidenceGate(
            true,
            "Project explanation evidence gate SATISFIED: " +
            summary +
            ".",
            Array.Empty<string>());
    }

    private static IReadOnlyList<string> BuildRequiredInspectionCandidates(
        IReadOnlyList<string> relevantInventoryFiles,
        IReadOnlyList<string> inspectedRelevantFiles,
        bool descriptorRequired,
        bool descriptorInspected,
        int sourceFileRequirement,
        int inspectedSourceFileCount,
        int requiredFileCount)
    {
        var inspected =
            new HashSet<string>(
                inspectedRelevantFiles,
                StringComparer.OrdinalIgnoreCase);

        var uninspected =
            relevantInventoryFiles
                .Where(
                    path => !inspected.Contains(
                        path))
                .ToArray();

        var selected =
            new List<string>();

        if (descriptorRequired
            && !descriptorInspected)
        {
            var descriptor =
                uninspected
                    .Where(
                        IsProjectDescriptor)
                    .OrderBy(
                        path => path,
                        StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(
                    descriptor))
            {
                selected.Add(
                    descriptor);
            }
        }

        var missingSourceCount =
            Math.Max(
                0,
                sourceFileRequirement - inspectedSourceFileCount);

        foreach (var sourcePath
                 in uninspected
                    .Where(
                        IsSourceFile)
                    .OrderBy(
                        GetSourceInspectionPriority)
                    .ThenBy(
                        path => path,
                        StringComparer.OrdinalIgnoreCase))
        {
            if (missingSourceCount <= 0)
            {
                break;
            }

            if (selected.Any(
                    selectedPath =>
                        selectedPath.Equals(
                            sourcePath,
                            StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            selected.Add(
                sourcePath);

            missingSourceCount--;
        }

        var missingRelevantCount =
            Math.Max(
                0,
                requiredFileCount - inspectedRelevantFiles.Count);

        foreach (var candidate
                 in uninspected
                    .OrderBy(
                        path => IsProjectDescriptor(path) ? 0 : IsSourceFile(path) ? 1 : 2)
                    .ThenBy(
                        path => path,
                        StringComparer.OrdinalIgnoreCase))
        {
            if (selected.Count
                >= missingRelevantCount)
            {
                break;
            }

            if (selected.Any(
                    selectedPath =>
                        selectedPath.Equals(
                            candidate,
                            StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            selected.Add(
                candidate);
        }

        return selected
            .Distinct(
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static int GetSourceInspectionPriority(
        string path)
    {
        var fileName =
            Path.GetFileName(
                path);

        if (fileName.Equals(
                "Program.cs",
                StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (fileName.StartsWith(
                "Main.",
                StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (fileName.StartsWith(
                "App.",
                StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return 10;
    }

    private static SekoProjectInventory ParseProjectInventory(
        string? detail)
    {
        var files =
            new List<string>();

        var directoryCount =
            0;

        foreach (var rawLine
                 in (detail ?? string.Empty)
                    .Split(
                        '\n',
                        StringSplitOptions.RemoveEmptyEntries))
        {
            var line =
                rawLine.Trim();

            if (line.StartsWith(
                    "[FILE] ",
                    StringComparison.OrdinalIgnoreCase))
            {
                var path =
                    NormalizeEvidencePath(
                        line[7..]);

                if (!string.IsNullOrWhiteSpace(
                        path))
                {
                    files.Add(
                        path);
                }

                continue;
            }

            if (line.StartsWith(
                    "[DIR] ",
                    StringComparison.OrdinalIgnoreCase))
            {
                directoryCount++;
            }
        }

        return new SekoProjectInventory(
            files
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .OrderBy(
                    path => path,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            directoryCount);
    }

    private static bool IsRecursiveRootInventory(
        string? argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(
                argumentsJson))
        {
            return false;
        }

        try
        {
            using var document =
                JsonDocument.Parse(
                    argumentsJson);

            var root =
                document.RootElement;

            var path =
                root.TryGetProperty(
                    "path",
                    out var pathElement)
                && pathElement.ValueKind
                    == JsonValueKind.String
                    ? pathElement.GetString()
                      ?? string.Empty
                    : string.Empty;

            var recursive =
                root.TryGetProperty(
                    "recursive",
                    out var recursiveElement)
                && recursiveElement.ValueKind
                    == JsonValueKind.True;

            var normalizedPath =
                NormalizeEvidencePath(
                    path);

            return recursive
                && (string.IsNullOrWhiteSpace(
                        normalizedPath)
                    || normalizedPath.Equals(
                        ".",
                        StringComparison.Ordinal));
        }
        catch
        {
            return false;
        }
    }

    private static string? GetStringArgument(
        string? argumentsJson,
        string propertyName)
    {
        if (string.IsNullOrWhiteSpace(
                argumentsJson))
        {
            return null;
        }

        try
        {
            using var document =
                JsonDocument.Parse(
                    argumentsJson);

            if (!document.RootElement.TryGetProperty(
                    propertyName,
                    out var property)
                || property.ValueKind
                    != JsonValueKind.String)
            {
                return null;
            }

            return property.GetString();
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractFilePath(
        string? detail)
    {
        foreach (var rawLine
                 in (detail ?? string.Empty)
                    .Split(
                        '\n',
                        StringSplitOptions.RemoveEmptyEntries))
        {
            var line =
                rawLine.Trim();

            if (!line.StartsWith(
                    "FILE:",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return NormalizeEvidencePath(
                line[5..]);
        }

        return null;
    }

    private static IReadOnlyList<string> AddDistinctPath(
        IReadOnlyList<string> paths,
        string path)
    {
        var normalized =
            NormalizeEvidencePath(
                path);

        if (string.IsNullOrWhiteSpace(
                normalized))
        {
            return paths;
        }

        return paths
            .Append(
                normalized)
            .Distinct(
                StringComparer.OrdinalIgnoreCase)
            .OrderBy(
                item => item,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeEvidencePath(
        string? path)
    {
        var normalized =
            (path ?? string.Empty)
                .Trim()
                .Replace(
                    '\\',
                    '/');

        while (normalized.StartsWith(
                   "./",
                   StringComparison.Ordinal))
        {
            normalized =
                normalized[2..];
        }

        return normalized;
    }

    private static bool IsProjectExplanationRelevantFile(
        string path)
    {
        if (IsProjectDescriptor(
                path)
            || IsSourceFile(
                path))
        {
            return true;
        }

        var fileName =
            Path.GetFileName(
                path);

        if (fileName.StartsWith(
                "README",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var extension =
            Path.GetExtension(
                path)
                .ToLowerInvariant();

        return extension is
            ".md"
            or ".txt"
            or ".json"
            or ".yaml"
            or ".yml"
            or ".toml"
            or ".xml"
            or ".config"
            or ".props"
            or ".targets"
            or ".ps1"
            or ".sh";
    }

    private static bool IsProjectDescriptor(
        string path)
    {
        var fileName =
            Path.GetFileName(
                path);

        if (fileName.Equals(
                "package.json",
                StringComparison.OrdinalIgnoreCase)
            || fileName.Equals(
                "pyproject.toml",
                StringComparison.OrdinalIgnoreCase)
            || fileName.Equals(
                "Cargo.toml",
                StringComparison.OrdinalIgnoreCase)
            || fileName.Equals(
                "go.mod",
                StringComparison.OrdinalIgnoreCase)
            || fileName.Equals(
                "pom.xml",
                StringComparison.OrdinalIgnoreCase)
            || fileName.Equals(
                "build.gradle",
                StringComparison.OrdinalIgnoreCase)
            || fileName.Equals(
                "build.gradle.kts",
                StringComparison.OrdinalIgnoreCase)
            || fileName.Equals(
                "CMakeLists.txt",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var extension =
            Path.GetExtension(
                path)
                .ToLowerInvariant();

        return extension is
            ".sln"
            or ".csproj"
            or ".fsproj"
            or ".vbproj"
            or ".vcxproj";
    }

    private static bool IsSourceFile(
        string path)
    {
        var extension =
            Path.GetExtension(
                path)
                .ToLowerInvariant();

        return extension is
            ".cs"
            or ".fs"
            or ".vb"
            or ".xaml"
            or ".js"
            or ".jsx"
            or ".ts"
            or ".tsx"
            or ".py"
            or ".java"
            or ".kt"
            or ".kts"
            or ".cpp"
            or ".c"
            or ".h"
            or ".hpp"
            or ".go"
            or ".rs"
            or ".swift"
            or ".php"
            or ".rb"
            or ".vue"
            or ".svelte";
    }

    private sealed record SekoProjectInventory(
        IReadOnlyList<string> Files,
        int DirectoryCount);

    private sealed record SekoProjectExplanationEvidenceGate(
        bool Satisfied,
        string Reason,
        IReadOnlyList<string> RequiredInspectionCandidates);

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