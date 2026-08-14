namespace Seko.Infrastructure.Agent;

public static class TaskIntentAnalyzer
{
    public static TaskIntent Analyze(
        string request)
    {
        var normalized =
            request.ToLowerInvariant();

        /*
            Explicit read-only wording wins over mutation keywords.

            Without this guard, a request such as "inspect the project, but do
            not modify any files" contains both "modify" and "file" and would
            incorrectly be classified as a required modification task.
        */
        var explicitlyReadOnly =
            ContainsAnyPhrase(
                normalized,
                "do not modify",
                "don't modify",
                "do not change",
                "don't change",
                "do not edit",
                "don't edit",
                "without modifying",
                "without changing",
                "without editing",
                "without making changes",
                "do not make changes",
                "don't make changes",
                "no modifications",
                "read only",
                "read-only",
                "inspect only",
                "do not write",
                "don't write");

        var mutationWords =
            new[]
            {
                "implement",
                "modify",
                "change",
                "edit",
                "fix",
                "create",
                "add",
                "remove",
                "delete",
                "rename",
                "redesign",
                "refactor",
                "update",
                "make",
                "adjust",
                "improve",
                "resize",
                "restyle",
                "replace",
                "set",
                "compact",
                "smaller",
                "larger"
            };

        var inspectionWords =
            new[]
            {
                "inspect",
                "read",
                "find",
                "locate",
                "where",
                "search",
                "show",
                "check",
                "review"
            };

        var buildWords =
            new[]
            {
                "build",
                "compile",
                "rebuild"
            };

        var diagnosticPhrases =
            new[]
            {
                "diagnose",
                "what went wrong",
                "task log",
                "latest log",
                "previous task",
                "last task",
                "failed task",
                "task failed",
                "task failure",
                "why did your last task",
                "why did the last task"
            };

        var workspaceWords =
            new[]
            {
                "code",
                "codebase",
                "file",
                "folder",
                "workspace",
                "project",
                "repository",
                "repo",
                "git",
                "ui",
                "interface",
                "sidebar",
                "window",
                "xaml",
                "c#",
                ".cs",
                ".xaml",
                ".csproj",
                ".sln",
                "yourself",
                "your own",
                "your code",
                "your source",
                "your ui",
                "your task",
                "your behavior",
                "agent",
                "activity",
                "panel",
                "button",
                "version",
                "layout",
                "style",
                "color",
                "settings",
                "desktop",
                "application",
                "app",
                "seko",
                "logging",
                "history",
                "tool",
                "build"
            };

        var hasMutation =
            mutationWords.Any(
                normalized.Contains);

        var hasInspection =
            inspectionWords.Any(
                normalized.Contains);

        var explicitBuildRequested =
            buildWords.Any(
                normalized.Contains);

        var hasDiagnosticIntent =
            diagnosticPhrases.Any(
                normalized.Contains);

        var hasWorkspaceTarget =
            workspaceWords.Any(
                normalized.Contains);

        var requiresModification =
            !explicitlyReadOnly
            && hasMutation
            && hasWorkspaceTarget;

        var requiresWorkspaceTools =
            hasDiagnosticIntent
            || (hasWorkspaceTarget
                && (hasMutation
                    || hasInspection
                    || explicitBuildRequested));

        /*
            A direct self-development phrase should also count even when the
            user uses wording we did not explicitly enumerate above.
        */
        if (!requiresWorkspaceTools
            && hasMutation
            && (normalized.Contains("yourself")
                || normalized.Contains("your own")
                || normalized.Contains("seko")))
        {
            requiresWorkspaceTools =
                true;

            requiresModification =
                !explicitlyReadOnly;
        }

        return
            new TaskIntent(
                requiresWorkspaceTools,
                requiresModification,
                explicitBuildRequested);
    }

    private static bool ContainsAnyPhrase(
        string value,
        params string[] phrases)
    {
        return
            phrases.Any(
                phrase =>
                    value.Contains(
                        phrase,
                        StringComparison.Ordinal));
    }
}
