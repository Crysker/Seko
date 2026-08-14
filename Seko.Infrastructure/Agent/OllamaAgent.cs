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
    private const int MaximumToolRounds = 12;
    private const int MaximumNoProgressRounds = 3;
    private const int MaximumConversationMessages = 8;

    private static readonly HttpClient HttpClient =
        new()
        {
            BaseAddress = new Uri("http://localhost:11434"),
            Timeout = TimeSpan.FromMinutes(5)
        };

    private readonly Workspace _workspace;
    private readonly SekoToolHost _toolHost;
    private readonly string _model;

    public event Action<AgentActivity>? ActivityChanged;

    public OllamaAgent(
        Workspace workspace)
    {
        _workspace = workspace;

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
            "Preparing task…");

        await _toolHost.BeginTaskAsync(
            cancellationToken);

        var messages =
            BuildMessages(
                conversation);

        var userRequest =
            conversation
                .LastOrDefault(
                    message =>
                        message.Role == MessageRole.User)
                ?.Content
            ?? "Seko task";

        var workspaceToolsRequired =
            RequiresWorkspaceTools(
                userRequest);

        var toolRetryUsed = false;
        var anyRealToolExecuted = false;
        var modificationGeneration = 0;
        var noProgressRounds = 0;

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
                    ? "Thinking…"
                    : "Reviewing tool results…");

            using var responseDocument =
                await SendChatRequestAsync(
                    messages,
                    workspaceToolsRequired,
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
                    ["role"] = "assistant",
                    ["content"] = content
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
                if (workspaceToolsRequired
                    && !anyRealToolExecuted
                    && !toolRetryUsed)
                {
                    toolRetryUsed = true;

                    Report(
                        AgentActivityKind.Thinking,
                        "Retrying with workspace tools…");

                    messages.Add(
                        new JsonObject
                        {
                            ["role"] = "user",

                            ["content"] =
                                """
                                This request requires workspace access.

                                You have real workspace tools available.
                                Use them now.

                                Do not explain how the task could be done manually.
                                Do not claim that you cannot access files.

                                Use only the minimum tools needed.
                                """
                        });

                    continue;
                }

                return await FinishTaskAsync(
                    content,
                    userRequest,
                    workspaceToolsRequired,
                    cancellationToken);
            }

            var roundMadeProgress = false;

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

                var argumentsJson = "{}";

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
                            ["role"] = "tool",
                            ["tool_name"] = toolName,

                            ["content"] =
                                """
                                SKIPPED DUPLICATE TOOL CALL.

                                You already called this exact tool with these exact
                                arguments and the workspace has not changed since then.

                                Use the previous result.
                                Do not repeat the call.
                                Continue to the next necessary step or finish the task.
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
                    Source-file writes are deliberately allowed to finish
                    atomically even if Stop is pressed during the tiny write.

                    Cancellation is checked immediately afterward.
                    This avoids interrupting a file halfway through a write.
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

                anyRealToolExecuted = true;
                roundMadeProgress = true;

                if (IsSuccessfulModification(
                        result))
                {
                    modificationGeneration++;
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
                        ["role"] = "tool",
                        ["tool_name"] = toolName,
                        ["content"] = result
                    });
            }

            if (roundMadeProgress)
            {
                noProgressRounds = 0;
            }
            else
            {
                noProgressRounds++;
            }

            if (noProgressRounds == 2)
            {
                Report(
                    AgentActivityKind.Thinking,
                    "Avoiding repeated tool calls…");

                messages.Add(
                    new JsonObject
                    {
                        ["role"] = "user",

                        ["content"] =
                            """
                            You are repeating tool calls without making progress.

                            Review the tool results already present in the conversation.

                            Do not repeat completed inspections.

                            If the requested modification is complete:
                            - build once if required
                            - then stop calling tools
                            - provide the final response

                            If something failed, take one concrete corrective action.
                            """
                    });
            }

            if (noProgressRounds
                >= MaximumNoProgressRounds)
            {
                return await FinishSafetyStopAsync(
                    userRequest,
                    cancellationToken);
            }
        }

        return await FinishSafetyStopAsync(
            userRequest,
            cancellationToken);
    }

    private async Task<ChatMessage> FinishTaskAsync(
        string content,
        string userRequest,
        bool workspaceToolsRequired,
        CancellationToken cancellationToken)
    {
        Report(
            AgentActivityKind.Thinking,
            "Finalizing…");

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
                content += "\n\n";
            }

            content += gitResult;
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

    private async Task<ChatMessage> FinishSafetyStopAsync(
        string userRequest,
        CancellationToken cancellationToken)
    {
        Report(
            AgentActivityKind.Error,
            "Stopped repeated tool calls.");

        var gitResult =
            await _toolHost.TryAutoCommitAsync(
                userRequest,
                cancellationToken);

        var content =
            "I stopped because I detected repeated tool calls without meaningful progress.";

        if (!string.IsNullOrWhiteSpace(
                gitResult))
        {
            content +=
                "\n\n" +
                gitResult;
        }

        content +=
            "\n\nI did not continue looping automatically.";

        return CreateAssistantMessage(
            content);
    }

    private async Task<JsonDocument> SendChatRequestAsync(
        JsonArray messages,
        bool workspaceToolsRequired,
        CancellationToken cancellationToken)
    {
        var request =
            new JsonObject
            {
                ["model"] = _model,

                ["messages"] =
                    messages.DeepClone(),

                ["tools"] =
                    _toolHost.CreateToolDefinitions(),

                ["stream"] = false,
                ["think"] = false,
                ["keep_alive"] = "30m",

                ["options"] =
                    new JsonObject
                    {
                        ["temperature"] =
                            workspaceToolsRequired
                                ? 0.05
                                : 0.35,

                        ["num_ctx"] = 8192,
                        ["num_predict"] = 2048
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
        IReadOnlyList<ChatMessage> conversation)
    {
        var messages =
            new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "system",
                    ["content"] = BuildSystemPrompt()
                }
            };

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

    private string BuildSystemPrompt()
    {
        return
            $$"""
            You are Seko, Serkan's local personal AI agent.

            ACTIVE WORKSPACE
            Name: {{_workspace.Name}}
            Root: {{_workspace.RootPath}}

            Be calm, capable, concise and slightly playful.

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

            FAST TOOL STRATEGY
            Use as few tool calls as necessary.

            If you know a filename but not its path:
            use find_files.

            If you need one section of a known file:
            use find_text.

            Do not read an entire large file merely to locate one small piece
            of text.

            Use list_files only when you genuinely need a directory overview.

            For a small targeted edit, the preferred flow is:

            git_status
            -> find_files if necessary
            -> find_text
            -> replace_text
            -> build_project
            -> final response

            Do not call the same tool with the same arguments repeatedly.

            After a successful build, stop calling tools unless another action
            is genuinely necessary.

            TOOL RULE
            If the user explicitly asks you to inspect, edit, implement, fix,
            create, remove, redesign, build or otherwise work on something in
            the active workspace, use tools instead of explaining how to do it.

            Never say you cannot access workspace files while these tools exist.

            Never invent file contents.

            Never claim an inspection, modification or build succeeded unless
            its tool actually succeeded.

            SELF-DEVELOPMENT
            If this workspace is Seko's own repository, you may edit your own
            source when explicitly asked.

            A discussion about a possible feature is not permission to edit.

            CODE CHANGE PROCESS
            Before changing code:
            1. Check Git status.
            2. Inspect only the relevant source.
            3. Make the smallest sensible change.
            4. Prefer replace_text for focused edits.
            5. Build after C#, XAML or project-file changes.
            6. If the build fails, use the compiler output to repair the error.
            7. Rebuild after a repair.
            8. Once the build succeeds, stop and summarize.

            GIT SAFETY
            The host records whether Git was clean before the task began.

            If unrelated uncommitted changes existed before the task,
            modifications are blocked.

            Files you modify through your tools are tracked.

            After successful code changes and a successful build, the host may
            create a LOCAL Git commit containing only those changed files.

            Never push to GitHub automatically.

            SECURITY
            Stay inside the active workspace.

            Never seek passwords, API keys, credentials or private keys.

            Never bypass safeguards or silently expand your own permissions.
            """;
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
        string? path = null;
        string? name = null;

        try
        {
            using var document =
                JsonDocument.Parse(
                    argumentsJson);

            var root =
                document.RootElement;

            if (root.TryGetProperty(
                    "path",
                    out var pathElement)
                && pathElement.ValueKind
                    == JsonValueKind.String)
            {
                path =
                    pathElement.GetString();
            }

            if (root.TryGetProperty(
                    "name",
                    out var nameElement)
                && nameElement.ValueKind
                    == JsonValueKind.String)
            {
                name =
                    nameElement.GetString();
            }
        }
        catch
        {
            // Activity text is cosmetic.
        }

        return toolName switch
        {
            "git_status" =>
                "Checking Git status…",

            "git_diff" =>
                "Reviewing Git changes…",

            "find_files" =>
                string.IsNullOrWhiteSpace(name)
                    ? "Finding files…"
                    : $"Finding {name}…",

            "find_text" =>
                string.IsNullOrWhiteSpace(path)
                    ? "Inspecting relevant source…"
                    : $"Inspecting {path}…",

            "list_files" =>
                string.IsNullOrWhiteSpace(path)
                    ? "Inspecting workspace…"
                    : $"Listing {path}…",

            "read_file" =>
                string.IsNullOrWhiteSpace(path)
                    ? "Reading source file…"
                    : $"Reading {path}…",

            "replace_text" =>
                string.IsNullOrWhiteSpace(path)
                    ? "Editing source…"
                    : $"Editing {path}…",

            "write_file" =>
                string.IsNullOrWhiteSpace(path)
                    ? "Writing source file…"
                    : $"Writing {path}…",

            "build_project" =>
                "Building project…",

            _ =>
                $"Running {toolName}…"
        };
    }

    private static bool RequiresWorkspaceTools(
        string request)
    {
        var normalized =
            request.ToLowerInvariant();

        var actionWords =
            new[]
            {
                "inspect",
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
                "build",
                "compile",
                "update",
                "read"
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
                "your own"
            };

        return
            actionWords.Any(
                normalized.Contains)

            && workspaceWords.Any(
                normalized.Contains);
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

        return property.GetString()
               ?? string.Empty;
    }

    private static string Shorten(
        string text,
        int maximumLength)
    {
        var singleLine =
            text.Replace(
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
            + "…";
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
}