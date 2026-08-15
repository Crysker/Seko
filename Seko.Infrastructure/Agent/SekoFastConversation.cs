using System.Text.Json.Nodes;
using Seko.Core.Chat;

namespace Seko.Infrastructure.Agent;

public static class SekoFastConversation
{
    private const int MaximumConversationMessages =
        6;

    private const int MaximumMessageCharacters =
        1_800;

    private const string SystemPrompt =
        """
        You are Seko, Serkan's local personal AI assistant.

        Answer ordinary conversational and knowledge questions directly, clearly
        and concisely. Use the most natural safe interpretation of the user's
        wording. Ask a clarification only when ambiguity materially changes the
        answer.

        Do not mention, plan, or pretend to use internal tools, web research,
        workspace files, Git, capabilities, skills, task phases, or execution
        machinery. This fast conversation path has no tools.

        Do not invent current facts. Requests that require current or verified
        online information should normally be routed to research instead.

        Be calm, capable, concise and slightly playful. Give more detail when the
        user asks for it, but do not turn a simple question into a project plan.
        """;

    public static JsonArray BuildMessages(
        IReadOnlyList<ChatMessage> conversation)
    {
        ArgumentNullException.ThrowIfNull(
            conversation);

        var messages =
            new JsonArray
            {
                new JsonObject
                {
                    ["role"] =
                        "system",

                    ["content"] =
                        SystemPrompt
                }
            };

        foreach (var message
                 in conversation
                     .Where(
                         message =>
                             message.Role != MessageRole.System)
                     .TakeLast(
                         MaximumConversationMessages))
        {
            messages.Add(
                new JsonObject
                {
                    ["role"] =
                        message.Role == MessageRole.User
                            ? "user"
                            : "assistant",

                    ["content"] =
                        TrimMessage(
                            message.Content)
                });
        }

        return messages;
    }

    public static JsonObject CreateRequest(
        string model,
        JsonArray messages)
    {
        if (string.IsNullOrWhiteSpace(
                model))
        {
            throw new ArgumentException(
                "Model name is required.",
                nameof(model));
        }

        ArgumentNullException.ThrowIfNull(
            messages);

        return
            new JsonObject
            {
                ["model"] =
                    model,

                ["messages"] =
                    messages.DeepClone(),

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
                            0.35,

                        ["num_ctx"] =
                            4096,

                        ["num_predict"] =
                            768
                    }
            };
    }

    private static string TrimMessage(
        string content)
    {
        content ??=
            string.Empty;

        if (content.Length
            <= MaximumMessageCharacters)
        {
            return content;
        }

        return
            content[..MaximumMessageCharacters]
            + "\n[Earlier content truncated for fast conversation.]";
    }
}
