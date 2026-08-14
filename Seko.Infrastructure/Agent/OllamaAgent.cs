using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Seko.Core.Agent;
using Seko.Core.Chat;
using Seko.Core.Workspaces;

namespace Seko.Infrastructure.Agent;

public sealed class OllamaAgent : IAgent
{
    private static readonly HttpClient HttpClient =
        new()
        {
            BaseAddress = new Uri("http://localhost:11434"),
            Timeout = TimeSpan.FromMinutes(5)
        };

    private readonly Workspace _workspace;
    private readonly string _model;

    public OllamaAgent(Workspace workspace)
    {
        _workspace = workspace;

        _model =
            Environment.GetEnvironmentVariable("SEKO_OLLAMA_MODEL")
            ?? "qwen3:8b";
    }

    public async Task<ChatMessage> SendAsync(
        IReadOnlyList<ChatMessage> conversation,
        CancellationToken cancellationToken = default)
    {
        var request =
            new OllamaChatRequest
            {
                Model = _model,
                Messages = BuildMessages(conversation),
                Stream = false,
                Think = false,
                KeepAlive = "10m",
                Options =
                    new OllamaOptions
                    {
                        Temperature = 0.35,
                        NumContext = 8192
                    }
            };

        var requestJson =
            JsonSerializer.Serialize(request);

        using var requestContent =
            new StringContent(
                requestJson,
                Encoding.UTF8,
                "application/json");

        HttpResponseMessage response;

        try
        {
            response =
                await HttpClient.PostAsync(
                    "/api/chat",
                    requestContent,
                    cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            return CreateAssistantMessage(
                "I couldn't connect to Ollama.\n\n" +
                "Make sure Ollama is running and qwen3:8b is installed.\n\n" +
                exception.Message);
        }
        catch (TaskCanceledException)
        {
            return CreateAssistantMessage(
                "The local model took too long to respond.");
        }

        using (response)
        {
            var responseJson =
                await response.Content.ReadAsStringAsync(
                    cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return CreateAssistantMessage(
                    $"Ollama returned HTTP {(int)response.StatusCode}.\n\n" +
                    responseJson);
            }

            try
            {
                using var document =
                    JsonDocument.Parse(responseJson);

                var root =
                    document.RootElement;

                if (!root.TryGetProperty(
                        "message",
                        out var messageElement))
                {
                    return CreateAssistantMessage(
                        "Ollama responded, but I couldn't find a message in the response.");
                }

                if (!messageElement.TryGetProperty(
                        "content",
                        out var contentElement))
                {
                    return CreateAssistantMessage(
                        "Ollama responded, but the message contained no text.");
                }

                var content =
                    contentElement.GetString();

                if (string.IsNullOrWhiteSpace(content))
                {
                    return CreateAssistantMessage(
                        "Ollama returned an empty response.");
                }

                return CreateAssistantMessage(
                    content.Trim());
            }
            catch (JsonException exception)
            {
                return CreateAssistantMessage(
                    "Ollama returned a response I couldn't parse.\n\n" +
                    exception.Message);
            }
        }
    }

    private List<OllamaMessage> BuildMessages(
        IReadOnlyList<ChatMessage> conversation)
    {
        var messages =
            new List<OllamaMessage>
            {
                new()
                {
                    Role = "system",
                    Content = BuildSystemPrompt()
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
                new OllamaMessage
                {
                    Role =
                        message.Role == MessageRole.User
                            ? "user"
                            : "assistant",

                    Content = message.Content
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
            Be concise when the answer is simple.
            Be detailed when the task requires it.
            Do not sound like a generic corporate chatbot.

            PURPOSE
            Seko is intended to become a general-purpose personal computer agent.

            Over time you will be able to help with:
            - software development
            - Unity and game development
            - UX/UI and design
            - Blender and 3D workflows
            - web development
            - research
            - travel planning
            - productivity
            - computer automation
            - voice interaction
            - visual understanding
            - managing projects and workspaces
            - improving Seko itself

            CURRENT LIMITATIONS
            Right now you are in the conversational bootstrap stage.
            You can talk and reason, but you do not yet have computer tools
            attached to this agent.

            Never pretend that you read a file, changed code, opened an
            application, searched the web or performed an action unless a
            real tool was available and actually performed that action.

            PRIVACY
            Do not request passwords, private keys, API keys or credentials.

            Seko is local-first.
            The current language model is running through Ollama rather than
            a paid cloud AI API.
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

    private sealed class OllamaChatRequest
    {
        [JsonPropertyName("model")]
        public required string Model { get; init; }

        [JsonPropertyName("messages")]
        public required List<OllamaMessage> Messages { get; init; }

        [JsonPropertyName("stream")]
        public bool Stream { get; init; }

        [JsonPropertyName("think")]
        public bool Think { get; init; }

        [JsonPropertyName("keep_alive")]
        public required string KeepAlive { get; init; }

        [JsonPropertyName("options")]
        public required OllamaOptions Options { get; init; }
    }

    private sealed class OllamaMessage
    {
        [JsonPropertyName("role")]
        public required string Role { get; init; }

        [JsonPropertyName("content")]
        public required string Content { get; init; }
    }

    private sealed class OllamaOptions
    {
        [JsonPropertyName("temperature")]
        public double Temperature { get; init; }

        [JsonPropertyName("num_ctx")]
        public int NumContext { get; init; }
    }
}