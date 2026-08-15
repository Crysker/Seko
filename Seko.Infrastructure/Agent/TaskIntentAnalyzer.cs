namespace Seko.Infrastructure.Agent;

public static class TaskIntentAnalyzer
{
    public static TaskIntent Analyze(
        string request)
    {
        var normalized =
            request
                .ToLowerInvariant()
                .Replace(
                    '\u2019',
                    '\'');

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

        /*
            Capability questions can mention mutation verbs without granting
            permission to execute them.

            "Can you improve your own code? Just a question, not asking you
            to do it." must stay conversational even though it contains
            "improve", "code", and "your own".

            Strong non-action qualifiers suppress execution on their own.
            Softer wording such as "just a question" suppresses execution only
            when paired with an explicit capability-question form.
        */
        var capabilityQuestion =
            ContainsAnyPhrase(
                normalized,
                "can you",
                "could you",
                "would you be able to",
                "are you able to",
                "is it possible for you to",
                "would it be possible for you to",
                "do you have the ability to",
                "are you capable of");

        var explicitExecutionSuppression =
            ContainsAnyPhrase(
                normalized,
                "not asking you to do it",
                "not asking you to actually do it",
                "i am not asking you to do it",
                "i'm not asking you to do it",
                "im not asking you to do it",
                "not telling you to do it",
                "don't actually do it",
                "do not actually do it",
                "no action needed",
                "no action required",
                "don't act on this",
                "do not act on this",
                "don't execute this",
                "do not execute this")
            || (capabilityQuestion
                && ContainsAnyPhrase(
                    normalized,
                    "just a question",
                    "just asking if",
                    "only asking if"));

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

        var projectExplanationTargets =
            new[]
            {
                "this project",
                "the project",
                "current project",
                "this workspace",
                "the workspace",
                "current workspace",
                "this repository",
                "the repository",
                "this repo",
                "the repo",
                "this codebase",
                "the codebase",
                "this application",
                "the application",
                "this app",
                "the app",
                "your code",
                "your source"
            };

        var projectExplanationPhrases =
            new[]
            {
                "explain",
                "describe",
                "summarize",
                "summary",
                "overview",
                "what does",
                "how does",
                "walk me through"
            };

        var requiresProjectExplanationEvidence =
            !explicitExecutionSuppression
            && projectExplanationTargets.Any(
                normalized.Contains)
            && projectExplanationPhrases.Any(
                normalized.Contains);

        var hasMutation =
            mutationWords.Any(
                word =>
                    ContainsMutationTerm(
                        normalized,
                        word));

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

        var isWorkspaceCapabilityQuestion =
            capabilityQuestion
            && hasWorkspaceTarget
            && (hasMutation
                || hasInspection
                || explicitBuildRequested);

        var requiresModification =
            !explicitExecutionSuppression
            && !explicitlyReadOnly
            && hasMutation
            && hasWorkspaceTarget;

        var requiresWorkspaceTools =
            !explicitExecutionSuppression
            && (requiresProjectExplanationEvidence
                || hasDiagnosticIntent
                || (hasWorkspaceTarget
                    && (hasMutation
                        || hasInspection
                        || explicitBuildRequested)));

        /*
            A direct self-development phrase should also count even when the
            user uses wording we did not explicitly enumerate above.
        */
        if (!explicitExecutionSuppression
            && !requiresWorkspaceTools
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
                explicitBuildRequested)
            {
                ExecutionSuppressed =
                    explicitExecutionSuppression,

                IsWorkspaceCapabilityQuestion =
                    isWorkspaceCapabilityQuestion,

                RequiresProjectExplanationEvidence =
                    requiresProjectExplanationEvidence
            };
    }

    private static bool ContainsMutationTerm(
        string value,
        string term)
    {
        if (!term.Equals(
                "implement",
                StringComparison.Ordinal))
        {
            return
                value.Contains(
                    term,
                    StringComparison.Ordinal);
        }

        /*
            "implementation" and "implementations" are nouns and frequently
            appear in ordinary technical explanations. Treat only actual verb
            forms as the implementation mutation action so those questions stay
            on the fast conversational path.
        */
        return
            ContainsWholeWord(
                value,
                "implement")
            || ContainsWholeWord(
                value,
                "implementing")
            || ContainsWholeWord(
                value,
                "implemented");
    }

    private static bool ContainsWholeWord(
        string value,
        string word)
    {
        var searchIndex =
            0;

        while (searchIndex
               <= value.Length - word.Length)
        {
            var index =
                value.IndexOf(
                    word,
                    searchIndex,
                    StringComparison.Ordinal);

            if (index < 0)
            {
                return false;
            }

            var endIndex =
                index + word.Length;

            var startsAtBoundary =
                index == 0
                || !IsWordCharacter(
                    value[index - 1]);

            var endsAtBoundary =
                endIndex == value.Length
                || !IsWordCharacter(
                    value[endIndex]);

            if (startsAtBoundary
                && endsAtBoundary)
            {
                return true;
            }

            searchIndex =
                endIndex;
        }

        return false;
    }

    private static bool IsWordCharacter(
        char value)
    {
        return
            char.IsLetterOrDigit(
                value)
            || value == '_';
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