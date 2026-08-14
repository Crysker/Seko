using System.Net.Http.Json;
using System.Text;
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
                        message.Role
                        == MessageRole.User)
                ?.Content
            ?? "Seko task";

        for (var round = 0;
             round < MaximumToolRounds;
             round++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var response =
                await SendChatRequestAsync(
                    messages,
                    cancellationToken);

            var root =
                response.RootElement;

            if (!root.TryGetProperty(
                    "message",
                    out var messageElement))
            {
                throw new InvalidOperationException(
                    "Ollama returned a response without a message.");
            }

            var content =
                messageElement.TryGetProperty(
                    "content",
                    out var contentElement)
                    ? contentElement.GetString()
                      ?? string.Empty
                    : string.Empty;

            var assistantMessage =
                new JsonObject
                {
                    ["role"] =
                        "assistant",

                    ["content"] =
                        content
                };

            JsonElement toolCallsElement =
                default;

            var hasToolCalls =
                messageElement.TryGetProperty(
                    "tool_calls",
                    out toolCallsElement)

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
                    content);
            }

            foreach (
                var toolCall
                in toolCallsElement.EnumerateArray())
            {
                if (!toolCall.TryGetProperty(
                        "function",
                        out var function))
                {
                    continue;
                }

                var toolName =
                    function.TryGetProperty(
                        "name",
                        out var nameElement)
                        ? nameElement.GetString()
                          ?? string.Empty
                        : string.Empty;

                var argumentsJson =
                    function.TryGetProperty(
                        "arguments",
                        out var argumentsElement)
                        ? argumentsElement.GetRawText()
                        : "{}";

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
            "I stopped because I reached my tool-call safety limit. " +
            "I may be going in circles, so I need your input.");
    }

    private async Task<JsonDocument> SendChatRequestAsync(
        JsonArray messages,
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
                    _toolHost
                        .CreateToolDefinitions(),

                ["stream"] =
                    false,

                ["think"] =
                    true,

                ["keep_alive"] =
                    "10m",

                ["options"] =
                    new JsonObject
                    {
                        ["temperature"] =
                            0.2,

                        ["num_ctx"] =
                            8192
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
                "Make sure Ollama is installed and running.\n\n" +
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
                    $"Ollama returned HTTP " +
                    $"{(int)response.StatusCode}.\n\n" +
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
            in conversation.TakeLast(20))
        {
            if (message.Role
                == MessageRole.System)
            {
                continue;
            }

            var role =
                message.Role
                == MessageRole.User
                    ? "user"
                    : "assistant";

            messages.Add(
                new JsonObject
                {
                    ["role"] =
                        role,

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
            For normal questions, respond naturally.
            For tasks, focus on accomplishing the task rather than narrating everything.

            PURPOSE
            Seko is a general-purpose personal AI agent.

            It is intended to eventually help with:
            - software development
            - UX/UI design
            - game development
            - Unity
            - Blender and 3D
            - web development
            - research
            - travel
            - productivity
            - voice
            - visual understanding
            - computer automation
            - additional skills added over time

            TOOLS
            You are inside an agent loop.
            You may call tools multiple times before responding.

            Never claim that you inspected, edited, built or checked something
            unless the corresponding tool actually succeeded.

            WORKSPACE SECURITY
            You may only access the active workspace through the provided tools.
            Never try to escape the workspace.
            Never seek passwords, API keys, credentials or private keys.
            Sensitive files are intentionally inaccessible.

            SELF-DEVELOPMENT
            If the active workspace is Seko's own repository, you may modify your
            own source code when Serkan explicitly asks you to implement, fix,
            redesign or change something.

            A normal discussion about an idea is NOT permission to edit files.

            When modifying code:
            1. Inspect Git status.
            2. Inspect the relevant files.
            3. Understand the existing implementation.
            4. Make the smallest sensible changes.
            5. Prefer replace_text for focused edits.
            6. Use write_file for new files or full rewrites.
            7. Inspect the result if necessary.
            8. Run build_project after code or XAML changes.
            9. If the build fails, inspect the compiler output and repair it.
            10. Do not endlessly retry.
            11. Never say the build succeeded unless the build tool says it did.

            GIT SAFETY
            The host records whether the Git working tree was clean before your task.

            If unrelated uncommitted changes already existed, file modifications
            will be blocked so that you cannot accidentally overwrite or commit
            Serkan's unfinished work.

            Files modified through your tools are tracked by the host.

            After a successful code change and successful build, the host will
            automatically create a LOCAL Git commit containing only the files
            you changed.

            You do not push to GitHub automatically.

            If the user dislikes a result, Git history allows the change to be
            reviewed or reverted later.

            IMPORTANT
            You are allowed to improve Seko, but you are not allowed to silently
            expand your own permissions or bypass capability restrictions.
            """;
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