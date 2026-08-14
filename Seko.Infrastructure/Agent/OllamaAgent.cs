using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Seko.Core.Agent;
using Seko.Core.Chat;
using Seko.Core.Workspaces;

namespace Seko.Infrastructure.Agent;

public sealed class OllamaAgent : IAgent
{
    private const int MaximumToolRounds = 8;
    private const int MaximumConversationMessages = 10;

    private static readonly HttpClient HttpClient =
        new()
        {
            BaseAddress =
                new Uri(
                    "http://localhost:11434"),

            Timeout =
                TimeSpan.FromMinutes(5)
        };

    private readonly Workspace _workspace;
    private readonly SekoToolHost _toolHost;
    private readonly string _model;

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

        var toolRetryUsed =
            false;

        var anyToolCallExecuted =
            false;

        for (var round = 0;
             round < MaximumToolRounds;
             round++)
        {
            cancellationToken.ThrowIfCancellationRequested();

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
                if (workspaceToolsRequired
                    && !anyToolCallExecuted
                    && !toolRetryUsed)
                {
                    toolRetryUsed =
                        true;

                    messages.Add(
                        new JsonObject
                        {
                            ["role"] =
                                "user",

                            ["content"] =
                                """
                                This request requires workspace access.

                                You have real workspace tools available.

                                Use them now.

                                Do not explain how the task could be done manually.
                                Do not claim that you cannot access files.
                                Do not ask me to paste source code unless a tool reports
                                that the required file does not exist.

                                Start with only the tools actually needed for this task.
                                """
                        });

                    continue;
                }

                var gitResult =
                    await _toolHost.TryAutoCommitAsync(
                        userRequest,
                        cancellationToken);

                if (!string.IsNullOrWhiteSpace(
                        gitResult))
                {
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
                            ? "I couldn't complete the workspace task with the available tools."
                            : "Done.";
                }

                return CreateAssistantMessage(
                    content.Trim());
            }

            anyToolCallExecuted =
                true;

            foreach (
                var toolCall
                in toolCallsElement.EnumerateArray())
            {
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

                var result =
                    await _toolHost.ExecuteAsync(
                        toolName,
                        argumentsJson,
                        cancellationToken);

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
        }

        return CreateAssistantMessage(
            "I stopped because I reached the tool-call safety limit. " +
            "I may be going in circles, so I need your input.");
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

                /*
                    Qwen3 thinking mode is deliberately disabled here.

                    For normal Seko interaction and tool use, direct tool calling
                    is much faster on our local hardware.

                    We can later add a separate "Deep Think" mode for difficult
                    architecture/research problems instead of paying this latency
                    on every workspace request.
                */
                ["think"] =
                    false,

                /*
                    Keep the model loaded between requests so subsequent messages
                    do not repeatedly pay the model-load startup cost.
                */
                ["keep_alive"] =
                    "30m",

                ["options"] =
                    new JsonObject
                    {
                        ["temperature"] =
                            workspaceToolsRequired
                                ? 0.1
                                : 0.35,

                        /*
                            Large enough for our current source-code work without
                            running the full 8K context for every small request.
                        */
                        ["num_ctx"] =
                            6144,

                        /*
                            Prevent unnecessarily long conversational responses.
                            Tool calls themselves are unaffected.
                        */
                        ["num_predict"] =
                            1024
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
                    ["role"] =
                        "system",

                    ["content"] =
                        BuildSystemPrompt()
                }
            };

        foreach (
            var message
            in conversation.TakeLast(
                MaximumConversationMessages))
        {
            if (message.Role == MessageRole.System)
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

            You have real workspace tools for:
            - listing files
            - reading files
            - writing files
            - replacing text
            - building .NET projects
            - checking Git status
            - viewing Git diffs

            TOOL RULE
            If the user explicitly asks you to inspect, edit, implement, fix,
            create, remove, redesign, build or otherwise work on something in
            the active workspace, use tools instead of explaining how to do it.

            Never say you cannot access workspace files while these tools exist.
            Never invent file contents or claim an action succeeded unless its
            tool succeeded.

            SELF-DEVELOPMENT
            If this workspace is Seko's own repository, you may edit your own
            source when explicitly asked.

            Before modifying code:
            1. Check Git status.
            2. Inspect only the relevant files.
            3. Make the smallest sensible change.
            4. Build after C#, XAML or project-file changes.
            5. Repair build errors you introduced.
            6. Do not endlessly retry.

            The host automatically blocks modifications when unrelated
            uncommitted changes existed before the task and may create a local
            Git commit after a successful change.

            Never push to GitHub automatically.

            SECURITY
            Stay inside the active workspace.
            Never seek passwords, API keys, credentials or private keys.
            Never bypass safeguards or silently expand your own permissions.
            """;
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

    private static string GetOptionalString(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(
                propertyName,
                out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return string.Empty;
        }

        return property.GetString()
               ?? string.Empty;
    }

    private static ChatMessage CreateAssistantMessage(
        string content)
    {
        return new ChatMessage
        {
            Role =
                MessageRole.Assistant,

            Content =
                content
        };
    }
}