namespace Seko.Infrastructure.Agent;

public static class SekoAutonomyLiveLoop
{
    public static SekoAutonomyController CreateController(
        TaskIntent taskIntent,
        bool requiresWebResearch,
        SekoAutonomyBudgetPolicy? budgetPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(
            taskIntent);

        return new SekoAutonomyController(
            new SekoAutonomyTaskRequirements(
                RequiresResearch:
                    requiresWebResearch,
                RequiresWorkspaceInspection:
                    taskIntent.RequiresWorkspaceTools
                    && (taskIntent.RequiresModification
                        || !taskIntent.ExplicitBuildRequested),
                RequiresModification:
                    taskIntent.RequiresModification,
                RequiresVerification:
                    taskIntent.ExplicitBuildRequested
                    || taskIntent.RequiresModification),
            budgetPolicy);
    }

    public static SekoAutonomyToolOutcome ClassifyToolResult(
        SekoAutonomyState state,
        string toolName,
        string result,
        bool toolSucceeded)
    {
        ArgumentNullException.ThrowIfNull(
            state);

        toolName ??=
            string.Empty;

        result ??=
            string.Empty;

        if (!toolSucceeded)
        {
            if (state.Phase
                    == SekoAutonomyPhase.Verification
                && toolName.Equals(
                    "build_project",
                    StringComparison.Ordinal))
            {
                return SekoAutonomyToolOutcome.Failure(
                    toolName,
                    SekoAutonomySignal.VerificationFailed,
                    result);
            }

            return SekoAutonomyToolOutcome.Failure(
                toolName,
                detail:
                    result);
        }

        if (IsModificationTool(
                toolName)
            && !IsSuccessfulModificationResult(
                result))
        {
            return SekoAutonomyToolOutcome.NoChange(
                toolName,
                result);
        }

        if (state.Phase
                == SekoAutonomyPhase.Research
            && IsResearchTool(
                toolName))
        {
            return SekoAutonomyToolOutcome.Success(
                toolName,
                SekoAutonomySignal.ResearchCompleted,
                result);
        }

        if (state.Phase
                == SekoAutonomyPhase.Inspection
            && IsInspectionEvidenceTool(
                toolName))
        {
            return SekoAutonomyToolOutcome.Success(
                toolName,
                SekoAutonomySignal.WorkspaceEvidenceObserved,
                result);
        }

        if (state.Phase
                == SekoAutonomyPhase.Action
            && IsModificationTool(
                toolName))
        {
            return SekoAutonomyToolOutcome.Success(
                toolName,
                SekoAutonomySignal.ModificationCompleted,
                result);
        }

        if (state.Phase
                == SekoAutonomyPhase.Verification
            && toolName.Equals(
                "build_project",
                StringComparison.Ordinal))
        {
            return SekoAutonomyToolOutcome.Success(
                toolName,
                SekoAutonomySignal.VerificationSucceeded,
                result);
        }

        if (state.Phase
                == SekoAutonomyPhase.Repair
            && IsModificationTool(
                toolName))
        {
            return SekoAutonomyToolOutcome.Success(
                toolName,
                SekoAutonomySignal.RepairCompleted,
                result);
        }

        return SekoAutonomyToolOutcome.Success(
            toolName,
            detail:
                result);
    }

    public static SekoAutonomyDecision ApplyToolResult(
        SekoAutonomyController controller,
        SekoAutonomyState state,
        string toolName,
        string result,
        bool toolSucceeded)
    {
        ArgumentNullException.ThrowIfNull(
            controller);

        return controller.ApplyToolOutcome(
            state,
            ClassifyToolResult(
                state,
                toolName,
                result,
                toolSucceeded));
    }

    public static SekoAutonomyDecision ApplyModelResponseWithoutTools(
        SekoAutonomyController controller,
        SekoAutonomyState state)
    {
        ArgumentNullException.ThrowIfNull(
            controller);

        ArgumentNullException.ThrowIfNull(
            state);

        if (state.Phase
                == SekoAutonomyPhase.Inspection
            && state.WorkspaceEvidenceObserved)
        {
            return controller.ApplySignal(
                state,
                SekoAutonomySignal.InspectionCompleted);
        }

        if (state.Phase
            == SekoAutonomyPhase.Synthesis)
        {
            return controller.ApplySignal(
                state,
                SekoAutonomySignal.SynthesisCompleted);
        }

        return controller.ApplySignal(
            state,
            SekoAutonomySignal.NoProgress);
    }

    public static SekoAutonomyDecision? ApplyNoToolResponse(
        SekoAutonomyController controller,
        SekoAutonomyState state,
        bool workspaceEvidenceObserved)
    {
        ArgumentNullException.ThrowIfNull(
            controller);

        ArgumentNullException.ThrowIfNull(
            state);

        if (state.Phase
                == SekoAutonomyPhase.Inspection
            && workspaceEvidenceObserved
            && !state.WorkspaceEvidenceObserved)
        {
            state =
                controller.ApplySignal(
                    state,
                    SekoAutonomySignal.WorkspaceEvidenceObserved)
                .State;
        }

        if (state.Phase
                == SekoAutonomyPhase.Inspection
            && state.WorkspaceEvidenceObserved)
        {
            return controller.ApplySignal(
                state,
                SekoAutonomySignal.InspectionCompleted);
        }

        if (state.Phase
            == SekoAutonomyPhase.Synthesis)
        {
            return controller.ApplySignal(
                state,
                SekoAutonomySignal.SynthesisCompleted);
        }

        return null;
    }

    private static bool IsResearchTool(
        string toolName)
    {
        return
            toolName.Equals(
                "web_research",
                StringComparison.Ordinal)
            || toolName.Equals(
                "web_fetch",
                StringComparison.Ordinal);
    }

    private static bool IsInspectionEvidenceTool(
        string toolName)
    {
        return toolName is
            "search_workspace"
            or "find_files"
            or "find_text"
            or "list_files"
            or "read_file"
            or "read_task_log";
    }

    private static bool IsModificationTool(
        string toolName)
    {
        return
            toolName.Equals(
                "write_file",
                StringComparison.Ordinal)
            || toolName.Equals(
                "replace_text",
                StringComparison.Ordinal);
    }

    private static bool IsSuccessfulModificationResult(
        string result)
    {
        return
            result.StartsWith(
                "Updated ",
                StringComparison.Ordinal)
            || result.StartsWith(
                "Wrote ",
                StringComparison.Ordinal);
    }
}