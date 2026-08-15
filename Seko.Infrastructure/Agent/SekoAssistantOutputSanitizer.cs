using System.Text;
using Seko.Core.Chat;

namespace Seko.Infrastructure.Agent;

public static class SekoAssistantOutputSanitizer
{
    public static ChatMessage Sanitize(
        ChatMessage message)
    {
        ArgumentNullException.ThrowIfNull(
            message);

        if (message.Role
            != MessageRole.Assistant)
        {
            return message;
        }

        var sanitizedContent =
            Sanitize(
                message.Content);

        if (string.Equals(
                sanitizedContent,
                message.Content,
                StringComparison.Ordinal))
        {
            return message;
        }

        return
            new ChatMessage
            {
                Id =
                    message.Id,

                Role =
                    message.Role,

                Content =
                    sanitizedContent,

                CreatedAt =
                    message.CreatedAt
            };
    }

    public static string Sanitize(
        string content)
    {
        ArgumentNullException.ThrowIfNull(
            content);

        if (content.Length == 0
            || (!content.Contains(
                    "<think",
                    StringComparison.OrdinalIgnoreCase)
                && !content.Contains(
                    "</think",
                    StringComparison.OrdinalIgnoreCase)))
        {
            return content;
        }

        var result =
            new StringBuilder(
                content.Length);

        var reasoningDepth =
            0;

        var changed =
            false;

        for (var index = 0;
             index < content.Length;)
        {
            if (TryReadThinkTag(
                    content,
                    index,
                    out var tagLength,
                    out var isClosing,
                    out var isSelfClosing))
            {
                changed =
                    true;

                if (isClosing)
                {
                    if (reasoningDepth > 0)
                    {
                        reasoningDepth--;
                    }
                    else
                    {
                        /*
                            A raw closing marker usually means the model emitted
                            hidden reasoning before the marker but omitted the
                            opening tag. Fail closed by discarding that prefix.
                        */
                        result.Clear();
                    }
                }
                else if (!isSelfClosing)
                {
                    reasoningDepth++;
                }

                index +=
                    tagLength;

                continue;
            }

            if (reasoningDepth == 0)
            {
                result.Append(
                    content[index]);
            }

            index++;
        }

        if (!changed)
        {
            return content;
        }

        return
            result
                .ToString()
                .Trim();
    }

    private static bool TryReadThinkTag(
        string content,
        int index,
        out int tagLength,
        out bool isClosing,
        out bool isSelfClosing)
    {
        tagLength =
            0;

        isClosing =
            false;

        isSelfClosing =
            false;

        if (index < 0
            || index >= content.Length
            || content[index] != '<')
        {
            return false;
        }

        var cursor =
            index + 1;

        if (cursor < content.Length
            && content[cursor] == '/')
        {
            isClosing =
                true;

            cursor++;
        }

        const string thinkName =
            "think";

        if (cursor + thinkName.Length
            > content.Length)
        {
            return false;
        }

        if (!content.AsSpan(
                cursor,
                thinkName.Length)
            .Equals(
                thinkName.AsSpan(),
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        cursor +=
            thinkName.Length;

        if (cursor < content.Length)
        {
            var boundary =
                content[cursor];

            if (boundary != '>'
                && boundary != '/'
                && !char.IsWhiteSpace(
                    boundary))
            {
                /*
                    Preserve unrelated tags such as <thinking> and
                    <think-data>. Only the actual reasoning marker is special.
                */
                return false;
            }
        }

        var tagEnd =
            content.IndexOf(
                '>',
                cursor);

        if (tagEnd < 0)
        {
            /*
                An unterminated <think... marker is treated as the beginning of
                hidden reasoning. Consume the rest of the response so malformed
                reasoning cannot escape to the UI or task log.
            */
            tagLength =
                content.Length
                - index;

            return true;
        }

        var beforeEnd =
            tagEnd - 1;

        while (beforeEnd >= cursor
               && char.IsWhiteSpace(
                   content[beforeEnd]))
        {
            beforeEnd--;
        }

        isSelfClosing =
            !isClosing
            && beforeEnd >= cursor
            && content[beforeEnd] == '/';

        tagLength =
            tagEnd - index + 1;

        return true;
    }
}