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
                    taskIntent.RequiresWorkspaceTools,
                RequiresModification:
                    taskIntent.RequiresModification,
                RequiresVerification:
                    taskIntent.ExplicitBuildRequested
                    || taskIntent.RequiresModification),
            budgetPolicy);
    }

    public static SekoAutonomyDecision? ApplyToolResult(
        SekoAutonomyController controller,
        SekoAutonomyState state,
        string toolName,
        string result,
        bool toolSucceeded)
    {
        ArgumentNullException.ThrowIfNull(
            controller);

        ArgumentNullException.ThrowIfNull(
            state);

        toolName ??=
            string.Empty;

        result ??=
            string.Empty;

        if (state.Phase
                == SekoAutonomyPhase.Research
            && toolSucceeded
            && IsResearchTool(
                toolName))
        {
            return controller.ApplySignal(
                state,
                SekoAutonomySignal.ResearchCompleted);
        }

        if (state.Phase
                == SekoAutonomyPhase.Inspection
            && toolSucceeded
            && IsInspectionEvidenceTool(
                toolName))
        {
            return controller.ApplySignal(
                state,
                SekoAutonomySignal.WorkspaceEvidenceObserved);
        }

        if (state.Phase
                == SekoAutonomyPhase.Action
            && toolSucceeded
            && IsModificationTool(
                toolName)
            && IsSuccessfulModificationResult(
                result))
        {
            return controller.ApplySignal(
                state,
                SekoAutonomySignal.ModificationCompleted);
        }

        if (state.Phase
                == SekoAutonomyPhase.Verification
            && toolName.Equals(
                "build_project",
                StringComparison.Ordinal))
        {
            return controller.ApplySignal(
                state,
                toolSucceeded
                    ? SekoAutonomySignal.VerificationSucceeded
                    : SekoAutonomySignal.VerificationFailed,
                toolSucceeded
                    ? null
                    : result);
        }

        if (state.Phase
                == SekoAutonomyPhase.Repair
            && toolSucceeded
            && IsModificationTool(
                toolName)
            && IsSuccessfulModificationResult(
                result))
        {
            return controller.ApplySignal(
                state,
                SekoAutonomySignal.RepairCompleted);
        }

        return null;
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