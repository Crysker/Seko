using System.Text.Json;

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

        if (taskIntent.ExecutionSuppressed)
        {
            throw new InvalidOperationException(
                "An execution-suppressed request cannot enter the autonomy tool loop.");
        }

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
                    || taskIntent.RequiresModification)
            {
                RequiresProjectExplanationEvidence =
                    taskIntent.RequiresProjectExplanationEvidence
            },
            budgetPolicy);
    }

    public static SekoAutonomyToolOutcome ClassifyToolResult(
        SekoAutonomyState state,
        string toolName,
        string result,
        bool toolSucceeded,
        string? argumentsJson = null)
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
                && IsVerificationToolForState(
                    state,
                    toolName,
                    argumentsJson))
            {
                return SekoAutonomyToolOutcome.Failure(
                    toolName,
                    SekoAutonomySignal.VerificationFailed,
                    result,
                    argumentsJson);
            }

            return SekoAutonomyToolOutcome.Failure(
                toolName,
                detail:
                    result,
                argumentsJson:
                    argumentsJson);
        }

        if (IsZeroMatchInspectionResult(
                toolName,
                result))
        {
            return SekoAutonomyToolOutcome.NoChange(
                toolName,
                result,
                argumentsJson);
        }

        if (IsModificationTool(
                toolName)
            && !IsSuccessfulModificationResult(
                result))
        {
            return SekoAutonomyToolOutcome.NoChange(
                toolName,
                result,
                argumentsJson);
        }

        if (state.Phase
                == SekoAutonomyPhase.Verification
            && IsVerificationToolName(
                toolName)
            && !IsVerificationToolForState(
                state,
                toolName,
                argumentsJson))
        {
            return SekoAutonomyToolOutcome.NoChange(
                toolName,
                "This verifier does not match the latest modification type and cannot satisfy the current verification generation.",
                argumentsJson);
        }

        if (state.Phase
                == SekoAutonomyPhase.Research
            && IsResearchTool(
                toolName))
        {
            return SekoAutonomyToolOutcome.Success(
                toolName,
                SekoAutonomySignal.ResearchCompleted,
                result,
                argumentsJson);
        }

        if (state.Phase
                == SekoAutonomyPhase.Inspection
            && IsInspectionEvidenceTool(
                toolName))
        {
            return SekoAutonomyToolOutcome.Success(
                toolName,
                SekoAutonomySignal.WorkspaceEvidenceObserved,
                result,
                argumentsJson);
        }

        if (state.Phase
                == SekoAutonomyPhase.Action
            && IsModificationTool(
                toolName))
        {
            return SekoAutonomyToolOutcome.Success(
                toolName,
                SekoAutonomySignal.ModificationCompleted,
                result,
                argumentsJson);
        }

        if (state.Phase
                == SekoAutonomyPhase.Verification
            && IsVerificationToolForState(
                state,
                toolName,
                argumentsJson))
        {
            return SekoAutonomyToolOutcome.Success(
                toolName,
                SekoAutonomySignal.VerificationSucceeded,
                result,
                argumentsJson);
        }

        if (state.Phase
                == SekoAutonomyPhase.Repair
            && IsModificationTool(
                toolName))
        {
            return SekoAutonomyToolOutcome.Success(
                toolName,
                SekoAutonomySignal.RepairCompleted,
                result,
                argumentsJson);
        }

        return SekoAutonomyToolOutcome.Success(
            toolName,
            detail:
                result,
            argumentsJson:
                argumentsJson);
    }

    public static SekoAutonomyDecision ApplyToolResult(
        SekoAutonomyController controller,
        SekoAutonomyState state,
        string toolName,
        string result,
        bool toolSucceeded,
        string? argumentsJson = null)
    {
        ArgumentNullException.ThrowIfNull(
            controller);

        return controller.ApplyToolOutcome(
            state,
            ClassifyToolResult(
                state,
                toolName,
                result,
                toolSucceeded,
                argumentsJson));
    }

    public static SekoAutonomyDecision ApplyModelResponseWithoutTools(
        SekoAutonomyController controller,
        SekoAutonomyState state)
    {
        ArgumentNullException.ThrowIfNull(
            controller);

        ArgumentNullException.ThrowIfNull(
            state);

        return controller.ApplyModelResponseWithoutTools(
            state);
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

    private static bool IsVerificationToolName(
        string toolName)
    {
        return toolName is
            "build_project"
            or "verify_file";
    }

    private static bool IsVerificationToolForState(
        SekoAutonomyState state,
        string toolName,
        string? argumentsJson)
    {
        if (toolName.Equals(
                "build_project",
                StringComparison.Ordinal))
        {
            return
                state.ModificationGeneration == 0
                || state.LatestModificationRequiresBuild;
        }

        if (toolName.Equals(
                "verify_file",
                StringComparison.Ordinal))
        {
            if (state.ModificationGeneration <= 0
                || state.LatestModificationRequiresBuild
                || string.IsNullOrWhiteSpace(
                    state.LatestModificationPath))
            {
                return false;
            }

            var requestedPath =
                GetVerificationPathArgument(
                    argumentsJson);

            return
                string.Equals(
                    NormalizeVerificationPath(
                        requestedPath),
                    NormalizeVerificationPath(
                        state.LatestModificationPath),
                    StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static string? GetVerificationPathArgument(
        string? argumentsJson)
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
                    "path",
                    out var pathElement)
                || pathElement.ValueKind
                    != JsonValueKind.String)
            {
                return null;
            }

            return pathElement.GetString();
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeVerificationPath(
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

    private static bool IsZeroMatchInspectionResult(
        string toolName,
        string result)
    {
        if (toolName.Equals(
                "search_workspace",
                StringComparison.Ordinal))
        {
            return result.StartsWith(
                "No relevant accessible workspace matches were found",
                StringComparison.OrdinalIgnoreCase);
        }

        if (toolName.Equals(
                "find_files",
                StringComparison.Ordinal))
        {
            return result.StartsWith(
                "No accessible files matching",
                StringComparison.OrdinalIgnoreCase);
        }

        if (toolName.Equals(
                "find_text",
                StringComparison.Ordinal))
        {
            return result.StartsWith(
                    "Text '",
                    StringComparison.OrdinalIgnoreCase)
                && result.Contains(
                    "was not found in",
                    StringComparison.OrdinalIgnoreCase);
        }

        return false;
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