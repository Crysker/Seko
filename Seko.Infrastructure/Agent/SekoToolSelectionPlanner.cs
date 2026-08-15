namespace Seko.Infrastructure.Agent;

public static class SekoToolSelectionPlanner
{
    private static readonly string[] WorkspaceInspectionTools =
    {
        "search_workspace",
        "find_files",
        "find_text",
        "list_files",
        "read_file"
    };

    private static readonly string[] WorkspaceModificationTools =
    {
        "search_workspace",
        "find_files",
        "find_text",
        "list_files",
        "read_file",
        "write_file",
        "replace_text",
        "build_project",
        "git_status",
        "git_diff"
    };

    private static readonly string[] WorkspaceVerificationTools =
    {
        "search_workspace",
        "find_files",
        "find_text",
        "list_files",
        "read_file",
        "build_project",
        "git_status"
    };

    private static readonly string[] DiagnosticPhrases =
    {
        "task log",
        "latest log",
        "previous task",
        "last task",
        "failed task",
        "task failed",
        "task failure",
        "latest unsuccessful",
        "what went wrong",
        "diagnose"
    };

    private static readonly string[] DirectFetchPhrases =
    {
        "fetch http://",
        "fetch https://",
        "read http://",
        "read https://",
        "open http://",
        "open https://",
        "read this url",
        "read this page",
        "open this url",
        "open the url"
    };

    public static SekoToolSelectionPlan Create(
        string currentTask,
        TaskIntent taskIntent,
        bool requiresWebResearch,
        bool webResearchCompleted)
    {
        ArgumentNullException.ThrowIfNull(
            taskIntent);

        currentTask ??=
            string.Empty;

        if (requiresWebResearch
            && !webResearchCompleted)
        {
            if (IsDirectWebFetchTask(
                    currentTask))
            {
                return
                    new SekoToolSelectionPlan(
                        SekoExecutionPhase.DirectWebFetch,
                        new[]
                        {
                            "web_fetch"
                        });
            }

            return
                new SekoToolSelectionPlan(
                    SekoExecutionPhase.Research,
                    new[]
                    {
                        "web_research"
                    });
        }

        if (taskIntent.RequiresWorkspaceTools)
        {
            if (IsDiagnosticTask(
                    currentTask))
            {
                return
                    new SekoToolSelectionPlan(
                        SekoExecutionPhase.WorkspaceInspection,
                        new[]
                        {
                            "read_task_log"
                        });
            }

            if (taskIntent.RequiresModification)
            {
                return
                    new SekoToolSelectionPlan(
                        SekoExecutionPhase.WorkspaceModification,
                        WorkspaceModificationTools);
            }

            if (taskIntent.ExplicitBuildRequested)
            {
                return
                    new SekoToolSelectionPlan(
                        SekoExecutionPhase.Verification,
                        WorkspaceVerificationTools);
            }

            return
                new SekoToolSelectionPlan(
                    SekoExecutionPhase.WorkspaceInspection,
                    WorkspaceInspectionTools);
        }

        if (requiresWebResearch
            && webResearchCompleted)
        {
            return
                new SekoToolSelectionPlan(
                    SekoExecutionPhase.Synthesis,
                    Array.Empty<string>());
        }

        return
            new SekoToolSelectionPlan(
                SekoExecutionPhase.Conversation,
                Array.Empty<string>());
    }

    public static bool IsDirectWebFetchTask(
        string currentTask)
    {
        if (string.IsNullOrWhiteSpace(
                currentTask))
        {
            return false;
        }

        var normalized =
            currentTask
                .Trim()
                .ToLowerInvariant();

        return
            DirectFetchPhrases.Any(
                normalized.Contains);
    }

    private static bool IsDiagnosticTask(
        string currentTask)
    {
        if (string.IsNullOrWhiteSpace(
                currentTask))
        {
            return false;
        }

        var normalized =
            currentTask
                .Trim()
                .ToLowerInvariant();

        return
            DiagnosticPhrases.Any(
                normalized.Contains);
    }
}
