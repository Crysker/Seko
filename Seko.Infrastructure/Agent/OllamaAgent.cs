using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Seko.Core.Agent;
using Seko.Core.Chat;
using Seko.Core.Workspaces;

namespace Seko.Infrastructure.Agent;

public sealed class OllamaAgent :
    IAgent,
    IAgentActivitySource
{
    private const int MaximumToolRounds = 32;
    private const int MaximumNoProgressRounds = 3;
    private const int MaximumConversationMessages = 8;
    private const int MaximumAutonomousContinuations = 12;

    private static readonly HttpClient HttpClient =
        new()
        {
            BaseAddress = new Uri("http://localhost:11434"),
            Timeout = TimeSpan.FromMinutes(5)
        };

    private static readonly HashSet<string> BuildRelevantExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".cs",
            ".xaml",
            ".csproj",
            ".sln",
            ".props",
            ".targets"
        };

    private readonly Workspace _workspace;
    private readonly SekoToolHost _toolHost;
    private readonly string _model;

    public event Action<AgentActivity>? ActivityChanged;

    public OllamaAgent(
        Workspace workspace)
    {
        _workspace =
            workspace;

        _toolHost =
            new SekoToolHost(
                workspace);

        _model =
            Environment.GetEnvironmentVariable(
                "SEKO_OLLAMA_MODEL")
            ?? "qwen3:8b";
    }

    public async Task<ChatMessage> SendAsync(
        IReadOnlyList<ChatMessage> conversation,
        CancellationToken cancellationToken = default)
    {
        Report(
            AgentActivityKind.Thinking,
            "Preparing task...");

        await _toolHost.BeginTaskAsync(
            cancellationToken);

        var userRequest =
            conversation
                .LastOrDefault(
                    message =>
                        message.Role == MessageRole.User)
                ?.Content
            ?? "Seko task";

        /*
            The current task is deliberately captured once.

            During a workspace/tool task, older conversation messages are not
            allowed to become executable instructions. They may have been useful
            conversational context before the task started, but this exact
            request is the task the agent must complete.
        */
        var currentTask =
            userRequest.Trim();

        var taskIntent =
            AnalyzeTaskIntent(
                currentTask);

        var messages =
            BuildMessages(
                conversation,
                currentTask,
                taskIntent);

        var toolRetryUsed =
            false;

        var anyRealToolExecuted =
            false;

        var modificationGeneration =
            0;

        var latestBuildRelevantModificationGeneration =
            -1;

        var latestSuccessfulBuildGeneration =
            -1;

        var anySuccessfulBuild =
            false;

        var noProgressRounds =
            0;

        var autonomousContinuations =
            0;

        var previousToolCalls =
            new HashSet<string>(
                StringComparer.Ordinal);

        for (var round = 0;
             round < MaximumToolRounds;
             round++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Report(
                AgentActivityKind.Thinking,
                round == 0
                    ? "Thinking..."
                    : "Reviewing tool results...");

            using var responseDocument =
                await SendChatRequestAsync(
                    messages,
                    taskIntent.RequiresWorkspaceTools,
                    cancellationToken);

            var root =
                responseDocument.RootElement;

            if (!root.TryGetProperty(
                    "message",
                    out var messageElement))
            {
                Report(
                    AgentActivityKind.Error,
                    "Ollama returned an invalid response.");

                return CreateAssistantMessage(
                    "Ollama responded, but the response did not contain a message.");
            }

            var content =
                GetOptionalString(
                    messageElement,
                    "content");

            var assistantMessage =
                new JsonObject
                {
                    ["role"] =
                        "assistant",

                    ["content"] =
                        content
                };

            var hasToolCalls =
                messageElement.TryGetProperty(
                    "tool_calls",
                    out var toolCallsElement)

                && toolCallsElement.ValueKind
                    == JsonValueKind.Array

                && toolCallsElement.GetArrayLength()
                    > 0;

            if (hasToolCalls)
            {
                assistantMessage["tool_calls"] =
                    JsonNode.Parse(
                        toolCallsElement.GetRawText());
            }

            messages.Add(
                assistantMessage);

            if (!hasToolCalls)
            {
                if (taskIntent.RequiresWorkspaceTools
                    && !anyRealToolExecuted
                    && !toolRetryUsed)
                {
                    toolRetryUsed =
                        true;

                    Report(
                        AgentActivityKind.Thinking,
                        "Starting workspace investigation...");

                    AddHostControl(
                        messages,
                        currentTask,
                        """
                        This task requires real workspace access.

                        You have not used a workspace tool yet.

                        Do not answer with instructions for the user.
                        Do not ask whether you should continue.
                        Do not claim that you cannot access the workspace.

                        Investigate and execute the CURRENT TASK using the
                        available tools now.
                        """);

                    continue;
                }

                if (taskIntent.RequiresModification
                    && modificationGeneration == 0)
                {
                    if (TryContinueAutonomously(
                            messages,
                            currentTask,
                            ref autonomousContinuations,
                            """
                            The CURRENT TASK requires an actual workspace
                            modification, but no file has been successfully
                            modified yet.

                            Do not finish.
                            Do not ask the user for permission to continue.
                            Do not merely describe what you intend to change.

                            Investigate the workspace, identify the most likely
                            target from evidence, make the requested change and
                            continue until the task is actually implemented.
                            """))
                    {
                        Report(
                            AgentActivityKind.Thinking,
                            "Continuing implementation...");

                        continue;
                    }

                    return FinishIncompleteTask(
                        "I could not complete the requested modification within the autonomous execution limit. No successful workspace modification was verified.");
                }

                if (latestBuildRelevantModificationGeneration
                    > latestSuccessfulBuildGeneration)
                {
                    if (TryContinueAutonomously(
                            messages,
                            currentTask,
                            ref autonomousContinuations,
                            """
                            Source code or project files were modified after the
                            most recent successful build.

                            The CURRENT TASK is not complete yet.

                            Build the final modified source now. If the build
                            fails, inspect the compiler output, repair the
                            problem and rebuild.

                            Do not ask the user to run the build manually.
                            Do not report success until the final modified source
                            has built successfully.

                            In the Seko repository, prefer the root Seko.sln when
                            building the complete application.
                            """))
                    {
                        Report(
                            AgentActivityKind.Thinking,
                            "Verifying final build...");

                        continue;
                    }

                    return FinishIncompleteTask(
                        "I modified build-relevant source, but I could not verify a successful build after the final modification. The task has not been marked complete.");
                }

                if (taskIntent.ExplicitBuildRequested
                    && !anySuccessfulBuild)
                {
                    if (TryContinueAutonomously(
                            messages,
                            currentTask,
                            ref autonomousContinuations,
                            """
                            The CURRENT TASK explicitly requires a build, but no
                            successful build has been verified yet.

                            Locate the appropriate solution or project and build
                            it now.

                            If a root solution exists, prefer it over asking the
                            user which individual project to build.

                            If the build fails, repair the problem when the
                            CURRENT TASK permits source changes, then rebuild.
                            """))
                    {
                        Report(
                            AgentActivityKind.Thinking,
                            "Running required build...");

                        continue;
                    }

                    return FinishIncompleteTask(
                        "I could not verify the requested build successfully.");
                }

                if (taskIntent.RequiresWorkspaceTools
                    && ShouldContinueInsteadOfAsking(
                        content))
                {
                    if (TryContinueAutonomously(
                            messages,
                            currentTask,
                            ref autonomousContinuations,
                            """
                            Your previous response stopped at planning,
                            permission-seeking or an unnecessary clarification.

                            Continue the CURRENT TASK autonomously.

                            Do not ask the user which likely file, project,
                            component or ordinary reversible implementation path
                            to choose when you can investigate the workspace and
                            determine the best candidate yourself.

                            Search, inspect and verify candidates first.

                            Ask the user only when essential information truly
                            cannot be inferred safely from the workspace, when a
                            permission boundary must expand, or before an
                            irreversible/external action requiring approval.
                            """))
                    {
                        Report(
                            AgentActivityKind.Thinking,
                            "Resolving task autonomously...");

                        continue;
                    }
                }

                if (taskIntent.RequiresModification
                    && LooksLikePlanningOnly(
                        content))
                {
                    content =
                        "The requested workspace change was completed and verified successfully.";
                }

                return await FinishTaskAsync(
                    content,
                    currentTask,
                    taskIntent.RequiresWorkspaceTools,
                    cancellationToken);
            }

            var roundMadeProgress =
                false;

            foreach (
                var toolCall
                in toolCallsElement.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!toolCall.TryGetProperty(
                        "function",
                        out var functionElement))
                {
                    continue;
                }

                var toolName =
                    GetOptionalString(
                        functionElement,
                        "name");

                if (string.IsNullOrWhiteSpace(
                        toolName))
                {
                    continue;
                }

                var argumentsJson =
                    "{}";

                if (functionElement.TryGetProperty(
                        "arguments",
                        out var argumentsElement))
                {
                    argumentsJson =
                        argumentsElement.ValueKind
                            == JsonValueKind.String
                            ? argumentsElement.GetString()
                              ?? "{}"
                            : argumentsElement.GetRawText();
                }

                var callSignature =
                    $"{modificationGeneration}|{toolName}|{argumentsJson}";

                if (!previousToolCalls.Add(
                        callSignature))
                {
                    Report(
                        AgentActivityKind.Tool,
                        $"Skipped duplicate {toolName} call.");

                    messages.Add(
                        new JsonObject
                        {
                            ["role"] =
                                "tool",

                            ["tool_name"] =
                                toolName,

                            ["content"] =
                                """
                                SKIPPED DUPLICATE TOOL CALL.

                                This exact tool call has already been executed
                                with the same arguments and the workspace has not
                                changed since then.

                                Use the previous result.

                                Do not repeat the same call.
                                Try a different investigation or editing strategy,
                                or finish if the CURRENT TASK is genuinely complete.
                                """
                        });

                    continue;
                }

                Report(
                    AgentActivityKind.Tool,
                    DescribeToolCall(
                        toolName,
                        argumentsJson));

                /*
                    Source-file writes are allowed to finish atomically even if
                    Stop is pressed during the tiny write operation.

                    Cancellation is checked immediately afterward so a file is
                    not intentionally interrupted halfway through a write.
                */
                var toolCancellationToken =
                    toolName is "write_file" or "replace_text"
                        ? CancellationToken.None
                        : cancellationToken;

                var result =
                    await _toolHost.ExecuteAsync(
                        toolName,
                        argumentsJson,
                        toolCancellationToken);

                cancellationToken.ThrowIfCancellationRequested();

                anyRealToolExecuted =
                    true;

                roundMadeProgress =
                    true;

                if (IsSuccessfulModification(
                        result))
                {
                    modificationGeneration++;

                    if (ToolTargetsBuildRelevantFile(
                            argumentsJson))
                    {
                        latestBuildRelevantModificationGeneration =
                            modificationGeneration;
                    }
                }

                if (string.Equals(
                        toolName,
                        "build_project",
                        StringComparison.Ordinal)
                    && IsSuccessfulBuildResult(
                        result))
                {
                    anySuccessfulBuild =
                        true;

                    latestSuccessfulBuildGeneration =
                        modificationGeneration;
                }

                if (result.StartsWith(
                        "ERROR:",
                        StringComparison.OrdinalIgnoreCase))
                {
                    Report(
                        AgentActivityKind.Error,
                        Shorten(
                            result,
                            120));
                }

                messages.Add(
                    new JsonObject
                    {
                        ["role"] =
                            "tool",

                        ["tool_name"] =
                            toolName,

                        ["content"] =
                            result
                    });
            }

            if (roundMadeProgress)
            {
                noProgressRounds =
                    0;
            }
            else
            {
                noProgressRounds++;
            }

            if (noProgressRounds == 2)
            {
                Report(
                    AgentActivityKind.Thinking,
                    "Avoiding repeated tool calls...");

                AddHostControl(
                    messages,
                    currentTask,
                    """
                    You are repeating tool calls without making meaningful
                    progress on the CURRENT TASK.

                    Review the tool results already available.

                    Do not repeat completed inspections.

                    If a guessed filename or target was wrong:
                    - do not keep guessing the same thing
                    - inspect the workspace structure
                    - inspect likely candidates
                    - ground the next action in actual tool evidence

                    If replace_text failed:
                    - inspect the actual nearby source again
                    - use the exact current text
                    - retry with a corrected unique target

                    If source was modified:
                    - make sure the final modified source builds successfully

                    Take one concrete new action toward the CURRENT TASK.
                    """);
            }

            if (noProgressRounds
                >= MaximumNoProgressRounds)
            {
                return FinishIncompleteTask(
                    "I stopped because repeated tool calls were no longer making meaningful progress. I did not mark the task as completed.");
            }
        }

        return FinishIncompleteTask(
            "I reached the autonomous tool limit before I could verify that the current task was complete. I did not mark it as completed.");
    }

    private async Task<ChatMessage> FinishTaskAsync(
        string content,
        string userRequest,
        bool workspaceToolsRequired,
        CancellationToken cancellationToken)
    {
        Report(
            AgentActivityKind.Thinking,
            "Finalizing...");

        var gitResult =
            await _toolHost.TryAutoCommitAsync(
                userRequest,
                cancellationToken);

        if (!string.IsNullOrWhiteSpace(
                gitResult))
        {
            Report(
                AgentActivityKind.Git,
                Shorten(
                    gitResult,
                    160));

            if (!string.IsNullOrWhiteSpace(
                    content))
            {
                content +=
                    "\n\n";
            }

            content +=
                gitResult;
        }

        if (string.IsNullOrWhiteSpace(
                content))
        {
            content =
                workspaceToolsRequired
                    ? "The workspace task is complete."
                    : "Done.";
        }

        Report(
            AgentActivityKind.Completed,
            "Task complete.");

        return CreateAssistantMessage(
            content.Trim());
    }

    private ChatMessage FinishIncompleteTask(
        string message)
    {
        /*
            Deliberately do NOT call TryAutoCommitAsync here.

            An incomplete, failed or loop-stopped task must not be converted into
            a successful deployment simply because some files happened to change.
        */
        Report(
            AgentActivityKind.Error,
            "Task incomplete.");

        return CreateAssistantMessage(
            message);
    }

    private async Task<JsonDocument> SendChatRequestAsync(
        JsonArray messages,
        bool workspaceToolsRequired,
        CancellationToken cancellationToken)
    {
        var request =
            new JsonObject
            {
                ["model"] =
                    _model,

                ["messages"] =
                    messages.DeepClone(),

                ["tools"] =
                    _toolHost.CreateToolDefinitions(),

                ["stream"] =
                    false,

                ["think"] =
                    false,

                ["keep_alive"] =
                    "30m",

                ["options"] =
                    new JsonObject
                    {
                        ["temperature"] =
                            workspaceToolsRequired
                                ? 0.05
                                : 0.35,

                        ["num_ctx"] =
                            8192,

                        ["num_predict"] =
                            2048
                    }
            };

        HttpResponseMessage response;

        try
        {
            response =
                await HttpClient.PostAsJsonAsync(
                    "/api/chat",
                    request,
                    cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new InvalidOperationException(
                "I couldn't connect to Ollama. " +
                "Make sure Ollama is running and qwen3:8b is installed.\n\n" +
                exception.Message);
        }

        using (response)
        {
            var responseText =
                await response.Content.ReadAsStringAsync(
                    cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Ollama returned HTTP {(int)response.StatusCode}.\n\n" +
                    responseText);
            }

            return JsonDocument.Parse(
                responseText);
        }
    }

    private JsonArray BuildMessages(
        IReadOnlyList<ChatMessage> conversation,
        string currentTask,
        TaskIntent taskIntent)
    {
        var messages =
            new JsonArray
            {
                new JsonObject
                {
                    ["role"] =
                        "system",

                    ["content"] =
                        BuildSystemPrompt(
                            currentTask)
                }
            };

        /*
            Workspace/tool tasks get a hard task boundary.

            Only the latest user request is inserted as the executable task.
            This prevents older requests such as "fix the Stop button" from
            becoming active again while the user is currently asking about the
            version number or another unrelated feature.
        */
        if (taskIntent.RequiresWorkspaceTools)
        {
            messages.Add(
                new JsonObject
                {
                    ["role"] =
                        "user",

                    ["content"] =
                        currentTask
                });

            return messages;
        }

        /*
            Ordinary conversation can still use recent conversational history.
        */
        foreach (
            var message
            in conversation.TakeLast(
                MaximumConversationMessages))
        {
            if (message.Role
                == MessageRole.System)
            {
                continue;
            }

            messages.Add(
                new JsonObject
                {
                    ["role"] =
                        message.Role == MessageRole.User
                            ? "user"
                            : "assistant",

                    ["content"] =
                        message.Content
                });
        }

        return messages;
    }

    private string BuildSystemPrompt(
        string currentTask)
    {
        return
            $$"""
            You are Seko, Serkan's local personal AI agent.

            ACTIVE WORKSPACE
            Name: {{_workspace.Name}}
            Root: {{_workspace.RootPath}}

            CURRENT TASK
            {{currentTask}}

            TASK BOUNDARY
            The CURRENT TASK above is the only executable user task for this
            tool run.

            Never switch to an older request.
            Never resume unrelated previous work.
            Never invent a new task.

            Be calm, capable, concise and slightly playful.

            AUTONOMOUS EXECUTION
            When the CURRENT TASK is actionable, execute it.

            Do not stop after saying:
            - "let me inspect..."
            - "let me try..."
            - "I found these files..."
            - "please specify which one..."
            - "would you like me to continue?"
            - "let me know if you'd like..."
            - "I'll make the following changes..."

            Those are not completed tasks.

            For ordinary reversible workspace work:
            investigate -> decide -> execute -> verify -> finish.

            Do not ask the user for implementation details that can reasonably
            be discovered from the workspace.

            If several plausible files or components exist:
            1. inspect the candidates
            2. use code/UI/project context
            3. choose the most likely target
            4. continue

            Ask the user only when:
            - essential information truly cannot be inferred safely
            - two interpretations could cause significantly different or risky
              outcomes and workspace evidence cannot resolve them
            - a permission boundary must be expanded
            - an irreversible/external action requires explicit approval

            REAL WORKSPACE TOOLS
            You have:
            - find_files
            - find_text
            - list_files
            - read_file
            - write_file
            - replace_text
            - build_project
            - git_status
            - git_diff

            TOOL SELECTION
            Use as few tool calls as necessary, but do not guess.

            Exact known path:
            -> use find_text or read_file

            Known filename but unknown path:
            -> use find_files

            UI concept, feature name, behavior, version, panel, sidebar,
            button or other conceptual target:
            -> do NOT pretend the concept is necessarily a filename
            -> inspect relevant project structure and candidate source files
            -> search actual source text in likely candidates
            -> ground the target in tool evidence before editing

            Example:
            "activity panel" does not automatically mean a file named
            ActivityPanel.xaml or AgentMonitorWindow.xaml.

            Verify which UI actually implements the requested element.

            PROJECT / BUILD RESOLUTION
            Do not ask the user which .csproj to build merely because several
            projects exist.

            Prefer a solution file at the workspace root when it represents the
            complete application.

            In Seko's repository, prefer:
            Seko.sln

            Use an individual .csproj when the task is genuinely scoped to that
            project or no appropriate solution exists.

            FAST TOOL STRATEGY
            Do not read an entire large file merely to locate one small piece
            of text.

            Use list_files only when you genuinely need a directory overview.

            For a small targeted edit, a good flow is:

            git_status
            -> locate/verify target
            -> inspect relevant source
            -> replace_text or write_file
            -> build_project when build-relevant source changed
            -> final response

            Do not call the same tool with the same arguments repeatedly.

            RECOVERY
            A failed search is information, not a reason to stop.

            If a guessed filename is not found:
            investigate broader project structure and actual source content.

            If replace_text reports OLD_TEXT_NOT_FOUND:
            inspect the real current source again,
            obtain the exact target text,
            then retry with the corrected unique match.

            Do not repeat the same failed replacement unchanged.

            If a build fails:
            use the compiler output,
            repair errors introduced by the task,
            then rebuild.

            TOOL RULE
            If the user asks you to inspect, edit, implement, fix, create,
            remove, redesign, build or otherwise work on the active workspace,
            use real tools instead of merely explaining how the user could do
            the task manually.

            Never say you cannot access workspace files while these tools exist.

            Never invent:
            - filenames
            - file contents
            - symbols
            - UI ownership
            - build success
            - modifications

            Claims about the workspace must come from tool evidence.

            SELF-DEVELOPMENT
            If this workspace is Seko's own repository, you may edit your own
            source when explicitly asked.

            Discussion about a possible feature is not permission to edit.

            CODE CHANGE PROCESS
            Before changing code:
            1. Check Git status.
            2. Inspect only relevant source.
            3. Make the smallest sensible change.
            4. Prefer replace_text for focused edits.
            5. Build after C#, XAML, solution or project-file changes.
            6. A build performed before the final modification does NOT verify
               the final source.
            7. If the build fails, use compiler output to repair the error.
            8. Rebuild after every repair or later build-relevant modification.
            9. Do not report a modification task as complete if no file was
               actually modified.
            10. Do not report a code task as complete until the final modified
                source has built successfully.

            GIT SAFETY
            The host records whether Git was clean before the task began.

            If unrelated uncommitted changes existed before the task,
            modifications are blocked.

            Files modified through your tools are tracked.

            After a genuinely completed task, the trusted host may create the
            appropriate local Git commit.

            Do not attempt to bypass Git safeguards.

            Do not ask the user to manually push or restart when the trusted
            Seko self-update host is configured to perform those deployment
            steps.

            SECURITY
            Stay inside the active workspace.

            Never seek passwords, API keys, credentials or private keys.

            Never bypass safeguards or silently expand your own permissions.

            NORMAL USER EXPERIENCE
            The user should be able to say things like:

            "change version to v1.1.2"
            "make the activity panel smaller"
            "fix the Stop button"
            "add a model selector"

            The user should not need to provide filenames, line numbers or tool
            instructions when those details can be discovered autonomously.
            """;
    }

    private static TaskIntent AnalyzeTaskIntent(
        string request)
    {
        var normalized =
            request.ToLowerInvariant();

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

        var hasWorkspaceTarget =
            workspaceWords.Any(
                normalized.Contains);

        var requiresModification =
            hasMutation
            && hasWorkspaceTarget;

        var requiresWorkspaceTools =
            hasWorkspaceTarget
            && (hasMutation
                || hasInspection
                || explicitBuildRequested);

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
                true;
        }

        return
            new TaskIntent(
                requiresWorkspaceTools,
                requiresModification,
                explicitBuildRequested);
    }

    private static bool TryContinueAutonomously(
        JsonArray messages,
        string currentTask,
        ref int autonomousContinuations,
        string instruction)
    {
        if (autonomousContinuations
            >= MaximumAutonomousContinuations)
        {
            return false;
        }

        autonomousContinuations++;

        AddHostControl(
            messages,
            currentTask,
            instruction);

        return true;
    }

    private static void AddHostControl(
        JsonArray messages,
        string currentTask,
        string instruction)
    {
        messages.Add(
            new JsonObject
            {
                ["role"] =
                    "system",

                ["content"] =
                    $"""
                    HOST EXECUTION CONTROL

                    CURRENT TASK:
                    {currentTask}

                    The CURRENT TASK has not changed.

                    {instruction}
                    """
            });
    }

    private static bool ShouldContinueInsteadOfAsking(
        string content)
    {
        if (string.IsNullOrWhiteSpace(
                content))
        {
            return true;
        }

        var normalized =
            content
                .Trim()
                .ToLowerInvariant();

        var clarificationPatterns =
            new[]
            {
                "please specify",
                "which one",
                "which file",
                "which project",
                "which component",
                "would you like me to",
                "do you want me to",
                "shall i",
                "should i proceed",
                "should i continue",
                "before i proceed",
                "if you meant",
                "if you want me to continue",
                "if you'd like me to continue"
            };

        if (clarificationPatterns.Any(
                normalized.Contains))
        {
            return true;
        }

        return LooksLikePlanningOnly(
            content);
    }

    private static bool LooksLikePlanningOnly(
        string content)
    {
        if (string.IsNullOrWhiteSpace(
                content))
        {
            return true;
        }

        var normalized =
            content
                .Trim()
                .ToLowerInvariant();

        var planningPrefixes =
            new[]
            {
                "let me ",
                "let's proceed",
                "i'll ",
                "i will ",
                "i'm going to ",
                "i am going to "
            };

        if (planningPrefixes.Any(
                normalized.StartsWith))
        {
            return true;
        }

        var planningFragments =
            new[]
            {
                "let me try",
                "let me first",
                "let me inspect",
                "let me locate",
                "let me look",
                "let me check",
                "let me proceed",
                "i'll make the following changes",
                "i will make the following changes"
            };

        return
            planningFragments.Any(
                normalized.Contains);
    }

    private static bool ToolTargetsBuildRelevantFile(
        string argumentsJson)
    {
        var path =
            GetToolArgument(
                argumentsJson,
                "path");

        if (string.IsNullOrWhiteSpace(
                path))
        {
            return false;
        }

        return
            BuildRelevantExtensions.Contains(
                Path.GetExtension(
                    path));
    }

    private static string? GetToolArgument(
        string argumentsJson,
        string propertyName)
    {
        try
        {
            using var document =
                JsonDocument.Parse(
                    argumentsJson);

            var root =
                document.RootElement;

            if (!root.TryGetProperty(
                    propertyName,
                    out var property)
                || property.ValueKind
                    != JsonValueKind.String)
            {
                return null;
            }

            return
                property.GetString();
        }
        catch
        {
            return null;
        }
    }

    private static bool IsSuccessfulBuildResult(
        string result)
    {
        if (string.IsNullOrWhiteSpace(
                result))
        {
            return false;
        }

        if (result.StartsWith(
                "ERROR:",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (result.Contains(
                "build failed",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (result.Contains(
                "failed to build",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private void Report(
        AgentActivityKind kind,
        string message)
    {
        ActivityChanged?.Invoke(
            new AgentActivity(
                kind,
                message));
    }

    private static string DescribeToolCall(
        string toolName,
        string argumentsJson)
    {
        var path =
            GetToolArgument(
                argumentsJson,
                "path");

        var name =
            GetToolArgument(
                argumentsJson,
                "name");

        return toolName switch
        {
            "git_status" =>
                "Checking Git status...",

            "git_diff" =>
                "Reviewing Git changes...",

            "find_files" =>
                string.IsNullOrWhiteSpace(
                    name)
                    ? "Finding files..."
                    : $"Finding {name}...",

            "find_text" =>
                string.IsNullOrWhiteSpace(
                    path)
                    ? "Inspecting relevant source..."
                    : $"Inspecting {path}...",

            "list_files" =>
                string.IsNullOrWhiteSpace(
                    path)
                    ? "Inspecting workspace..."
                    : $"Listing {path}...",

            "read_file" =>
                string.IsNullOrWhiteSpace(
                    path)
                    ? "Reading source file..."
                    : $"Reading {path}...",

            "replace_text" =>
                string.IsNullOrWhiteSpace(
                    path)
                    ? "Editing source..."
                    : $"Editing {path}...",

            "write_file" =>
                string.IsNullOrWhiteSpace(
                    path)
                    ? "Writing source file..."
                    : $"Writing {path}...",

            "build_project" =>
                "Building project...",

            _ =>
                $"Running {toolName}..."
        };
    }

    private static bool IsSuccessfulModification(
        string toolResult)
    {
        return
            toolResult.StartsWith(
                "Updated ",
                StringComparison.Ordinal)

            || toolResult.StartsWith(
                "Wrote ",
                StringComparison.Ordinal);
    }

    private static string GetOptionalString(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(
                propertyName,
                out var property)
            || property.ValueKind
                != JsonValueKind.String)
        {
            return string.Empty;
        }

        return
            property.GetString()
            ?? string.Empty;
    }

    private static string Shorten(
        string text,
        int maximumLength)
    {
        var singleLine =
            text
                .Replace(
                    "\r",
                    " ")
                .Replace(
                    "\n",
                    " ")
                .Trim();

        if (singleLine.Length
            <= maximumLength)
        {
            return singleLine;
        }

        return
            singleLine[..maximumLength]
            + "...";
    }

    private static ChatMessage CreateAssistantMessage(
        string content)
    {
        return
            new ChatMessage
            {
                Role =
                    MessageRole.Assistant,

                Content =
                    content
            };
    }

    private sealed record TaskIntent(
        bool RequiresWorkspaceTools,
        bool RequiresModification,
        bool ExplicitBuildRequested);
}