using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Seko.Core.Agent;
using Seko.Core.Chat;
using Seko.Core.Workspaces;

namespace Seko.Infrastructure.Agent;

public sealed class OllamaAgent : IAgent
{
    private const int MaximumToolRounds = 12;

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

        for (var round = 0;
             round < MaximumToolRounds;
             round++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var responseDocument =
                await SendChatRequestAsync(
                    messages,
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
                messageElement.TryGetProperty(
                    "content",
                    out var contentElement)
                && contentElement.ValueKind == JsonValueKind.String
                    ? contentElement.GetString()
                      ?? string.Empty
                    : string.Empty;

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
                        "Done.";
                }

                return CreateAssistantMessage(
                    content.Trim());
            }

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
                    functionElement.TryGetProperty(
                        "name",
                        out var nameElement)
                    && nameElement.ValueKind
                        == JsonValueKind.String
                        ? nameElement.GetString()
                          ?? string.Empty
                        : string.Empty;

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
                        ["role"] = "tool",
                        ["tool_name"] = toolName,
                        ["content"] = result
                    });
            }
        }

        return CreateAssistantMessage(
            "I stopped because I reached the tool-call safety limit. " +
            "I may be going in circles, so I need your input.");
    }

    private async Task<JsonDocument> SendChatRequestAsync(
        JsonArray messages,
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

                ["keep_alive"] = "10m",

                ["options"] =
                    new JsonObject
                    {
                        ["temperature"] = 0.2,
                        ["num_ctx"] = 8192
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
            in conversation.TakeLast(20))
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
            You are Seko, Serkan's personal local AI agent.

            You run locally on his Windows computer through Ollama.

            ACTIVE WORKSPACE
            Name: {{_workspace.Name}}
            Root: {{_workspace.RootPath}}

            PERSONALITY
            Be calm, capable, friendly and slightly playful.
            Do not sound like a generic corporate chatbot.
            For normal conversation, respond naturally.
            For tasks, focus on accomplishing the task.

            PURPOSE
            You are intended to become a general-purpose personal computer agent.

            You will eventually help with:
            - software development
            - Unity and game development
            - UX/UI design
            - Blender and 3D workflows
            - web development
            - research
            - travel
            - productivity
            - voice
            - visual understanding
            - computer automation
            - managing projects
            - improving Seko itself

            CURRENT TOOLS
            You now have tools for:
            - listing files
            - reading source files
            - writing source files
            - focused text replacement
            - running dotnet build
            - checking Git status
            - inspecting Git diffs

            You are inside an agent loop.

            You may call tools multiple times before giving your final response.

            Never claim that you inspected, changed, built or checked something
            unless the corresponding tool actually succeeded.

            WORKSPACE SECURITY
            Only use provided tools to access the active workspace.

            Never attempt to escape the workspace.

            Never seek:
            - passwords
            - API keys
            - credentials
            - private keys
            - secret files

            SELF-DEVELOPMENT
            If the active workspace is Seko's own repository, you may modify your
            own source code when Serkan explicitly asks you to implement, fix,
            redesign or change something.

            A normal discussion about an idea is NOT permission to edit files.

            When changing code:
            1. Check Git status.
            2. Inspect the relevant files.
            3. Understand the existing implementation.
            4. Make the smallest sensible change.
            5. Prefer replace_text for focused edits.
            6. Use write_file for new files or deliberate full rewrites.
            7. Inspect the result when useful.
            8. Run build_project after C#, XAML or project-file changes.
            9. If the build fails, inspect the compiler output and repair your change.
            10. Do not endlessly retry.
            11. Never claim the build succeeded unless build_project reports success.

            GIT SAFETY
            The host records whether Git was clean before your task began.

            If uncommitted changes already existed before your task, modifications
            are blocked so you cannot accidentally overwrite or commit Serkan's
            unfinished work.

            Files you modify through your tools are tracked.

            After a successful code change and successful build, the host may create
            a LOCAL Git commit containing only files you changed.

            You must not push to GitHub automatically.

            IMPORTANT
            You may improve Seko when explicitly asked.

            You may never silently expand your own permissions, disable safeguards,
            access blocked files, or bypass capability restrictions.
            """;
    }

    private static ChatMessage CreateAssistantMessage(
        string content)
    {
        return new ChatMessage
        {
            Role = MessageRole.Assistant,
            Content = content
        };
    }
}