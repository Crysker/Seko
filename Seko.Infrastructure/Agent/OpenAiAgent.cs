using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Seko.Core.Agent;
using Seko.Core.Chat;
using Seko.Core.Workspaces;

namespace Seko.Infrastructure.Agent;

public sealed class OpenAiAgent : IAgent
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(3)
    };

    private readonly Workspace _workspace;
    private readonly string _model;

    public OpenAiAgent(Workspace workspace)
    {
        _workspace = workspace;

        _model =
            Environment.GetEnvironmentVariable("SEKO_MODEL")
            ?? "gpt-5.6-terra";
    }

    public async Task<ChatMessage> SendAsync(
        IReadOnlyList<ChatMessage> conversation,
        CancellationToken cancellationToken = default)
    {
        var apiKey =
            Environment.GetEnvironmentVariable(
                "OPENAI_API_KEY");

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return CreateAssistantMessage(
                "I can't reach my AI model because OPENAI_API_KEY isn't configured.");
        }

        var requestBody = new JsonObject
        {
            ["model"] = _model,

            ["instructions"] = BuildInstructions(),

            ["input"] = BuildConversationInput(
                conversation),

            ["reasoning"] = new JsonObject
            {
                ["effort"] = "medium"
            },

            ["store"] = false
        };

        using var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                "https://api.openai.com/v1/responses");

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                apiKey);

        request.Content =
            new StringContent(
                requestBody.ToJsonString(),
                Encoding.UTF8,
                "application/json");

        using var response =
            await HttpClient.SendAsync(
                request,
                cancellationToken);

        var responseText =
            await response.Content.ReadAsStringAsync(
                cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"OpenAI API error " +
                $"{(int)response.StatusCode} " +
                $"{response.StatusCode}\n\n" +
                TrimText(responseText, 4000));
        }

        using var responseDocument =
            JsonDocument.Parse(
                responseText);

        var assistantText =
            ExtractAssistantText(
                responseDocument.RootElement);

        if (string.IsNullOrWhiteSpace(
                assistantText))
        {
            assistantText =
                "I received a response, but there was no text output.";
        }

        return CreateAssistantMessage(
            assistantText);
    }

    private JsonArray BuildConversationInput(
        IReadOnlyList<ChatMessage> conversation)
    {
        var input =
            new JsonArray();

        foreach (var message in conversation.TakeLast(30))
        {
            if (message.Role == MessageRole.System)
            {
                continue;
            }

            var role =
                message.Role == MessageRole.User
                    ? "user"
                    : "assistant";

            input.Add(
                new JsonObject
                {
                    ["role"] = role,
                    ["content"] = message.Content
                });
        }

        return input;
    }

    private string BuildInstructions()
    {
        return
            $$"""
            You are Seko, Serkan's personal AI assistant.

            You are running as a Windows desktop application.

            CURRENT WORKSPACE
            Name: {{_workspace.Name}}
            Root path: {{_workspace.RootPath}}

            PERSONALITY
            - Calm, capable and friendly.
            - Slightly playful when appropriate.
            - Do not sound like a generic corporate chatbot.
            - Be concise for simple questions.
            - Explain things properly when complexity requires it.
            - Collaborate with the user rather than lecturing them.

            ABOUT SEKO
            Seko is intended to become a general-purpose personal AI agent.

            Long term, Seko should help with:
            - software development
            - UX/UI design
            - game development
            - Unity
            - Blender and 3D
            - web development
            - research
            - travel
            - productivity
            - computer automation
            - voice interaction
            - visual understanding
            - other skills added over time

            SECURITY
            Right now you do not have computer-control tools.
            Never pretend that you read, wrote, opened, searched,
            downloaded, executed, or modified something unless a tool
            actually gave you that ability.

            The active workspace name and path are provided only as context.
            You cannot inspect its contents yet.

            When the user asks about your current workspace,
            answer using the workspace information above.
            """;
    }

    private static string ExtractAssistantText(
        JsonElement root)
    {
        if (!root.TryGetProperty(
                "output",
                out var output))
        {
            return string.Empty;
        }

        if (output.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var builder =
            new StringBuilder();

        foreach (var outputItem in output.EnumerateArray())
        {
            if (!outputItem.TryGetProperty(
                    "type",
                    out var itemType))
            {
                continue;
            }

            if (!string.Equals(
                    itemType.GetString(),
                    "message",
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (!outputItem.TryGetProperty(
                    "content",
                    out var content))
            {
                continue;
            }

            if (content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var contentItem in content.EnumerateArray())
            {
                if (!contentItem.TryGetProperty(
                        "type",
                        out var contentType))
                {
                    continue;
                }

                if (!string.Equals(
                        contentType.GetString(),
                        "output_text",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (!contentItem.TryGetProperty(
                        "text",
                        out var textElement))
                {
                    continue;
                }

                var text =
                    textElement.GetString();

                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.AppendLine();
                    builder.AppendLine();
                }

                builder.Append(text);
            }
        }

        return builder.ToString();
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

    private static string TrimText(
        string text,
        int maximumLength)
    {
        if (text.Length <= maximumLength)
        {
            return text;
        }

        return
            text[..maximumLength]
            + "\n\n[Response truncated]";
    }
}