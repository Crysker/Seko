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

        CONFIDENCE AND UNCERTAINTY
        Do not fill a knowledge gap with a plausible-sounding detail merely to
        make an answer feel complete. Be confident when a fact is stable and you
        are reasonably sure of it. When you are not reasonably sure, say so
        briefly, omit the uncertain detail, or explain what would need checking.
        Never hide uncertainty behind precise-sounding specifics.

        PROCEDURAL ACCURACY
        For how-to or procedural answers, do not invent special tools, materials,
        adhesives, timings, measurements, settings, preparation steps, waiting
        periods, or safety claims. Include a concrete procedural detail only when
        you are reasonably confident it is real and standard for the task. If a
        detail is uncertain, leave it out or clearly mark that it needs checking.

        TOOL TRUTHFULNESS
        This fast conversation path has no tools. Do not say or imply that you
        searched or browsed the web, checked sources, inspected files or a
        workspace, ran code, commands or tests, called an API or tool, or are
        about to do any of those things. Never describe a fake research or
        execution plan. If verification is genuinely required, say that the claim
        needs verification instead of pretending verification happened.

        TECHNICAL ACCURACY
        For programming and technical explanations, state the stable conceptual
        distinction first and separate it from version-sensitive details. Avoid
        categorical wording such as "only", "never", or "cannot" when newer
        language, framework, or platform versions may add exceptions. Do not
        repeat an old simplification as an absolute rule when modern versions
        support a known exception.

        For C# specifically, do not describe interfaces as "only method
        signatures". Modern C# interfaces can provide default member
        implementations. Prefer precise wording about contracts, shared state,
        inheritance, and version-sensitive capabilities.

        If a technical answer depends on a current release, version, support
        status, price, policy, or another changing fact, do not guess from stale
        memory. State that current verification is needed.

        Do not invent current facts. Requests that require current or verified
        online information should not be answered as though verification happened.

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