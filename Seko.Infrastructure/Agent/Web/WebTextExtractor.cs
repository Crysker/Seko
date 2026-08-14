using System.Net;
using System.Text.RegularExpressions;

namespace Seko.Infrastructure.Agent.Web;

public static class WebTextExtractor
{
    private static readonly Regex ScriptRegex =
        new(
            @"<script\b[^>]*>.*?</script>",
            RegexOptions.Compiled
            | RegexOptions.IgnoreCase
            | RegexOptions.Singleline);

    private static readonly Regex StyleRegex =
        new(
            @"<style\b[^>]*>.*?</style>",
            RegexOptions.Compiled
            | RegexOptions.IgnoreCase
            | RegexOptions.Singleline);

    private static readonly Regex NoScriptRegex =
        new(
            @"<noscript\b[^>]*>.*?</noscript>",
            RegexOptions.Compiled
            | RegexOptions.IgnoreCase
            | RegexOptions.Singleline);

    private static readonly Regex CommentRegex =
        new(
            @"<!--.*?-->",
            RegexOptions.Compiled
            | RegexOptions.Singleline);

    private static readonly Regex BlockTagRegex =
        new(
            @"</?(?:p|div|br|li|ul|ol|h[1-6]|tr|td|th|section|article|main|header|footer|nav|blockquote|pre)\b[^>]*>",
            RegexOptions.Compiled
            | RegexOptions.IgnoreCase);

    private static readonly Regex TagRegex =
        new(
            @"<[^>]+>",
            RegexOptions.Compiled
            | RegexOptions.Singleline);

    private static readonly Regex TitleRegex =
        new(
            @"<title\b[^>]*>(?<title>.*?)</title>",
            RegexOptions.Compiled
            | RegexOptions.IgnoreCase
            | RegexOptions.Singleline);

    private static readonly Regex HorizontalWhitespaceRegex =
        new(
            @"[ \t\f\v]+",
            RegexOptions.Compiled);

    private static readonly Regex ExcessNewlinesRegex =
        new(
            @"\n{3,}",
            RegexOptions.Compiled);

    public static string ExtractTitle(
        string html)
    {
        if (string.IsNullOrWhiteSpace(
                html))
        {
            return string.Empty;
        }

        var match =
            TitleRegex.Match(
                html);

        return
            match.Success
                ? ExtractInlineText(
                    match.Groups["title"].Value)
                : string.Empty;
    }

    public static string ExtractReadableText(
        string html)
    {
        if (string.IsNullOrWhiteSpace(
                html))
        {
            return string.Empty;
        }

        var value =
            RemoveNonContent(
                html);

        value =
            BlockTagRegex.Replace(
                value,
                "\n");

        value =
            TagRegex.Replace(
                value,
                " ");

        value =
            WebUtility.HtmlDecode(
                value);

        value =
            value.Replace(
                '\u00A0',
                ' ');

        value =
            value.Replace(
                    "\r\n",
                    "\n",
                    StringComparison.Ordinal)
                .Replace(
                    '\r',
                    '\n');

        var lines =
            value
                .Split(
                    '\n')
                .Select(
                    line =>
                        HorizontalWhitespaceRegex.Replace(
                                line,
                                " ")
                            .Trim())
                .Where(
                    line =>
                        line.Length > 0);

        value =
            string.Join(
                "\n",
                lines);

        return
            ExcessNewlinesRegex.Replace(
                    value,
                    "\n\n")
                .Trim();
    }

    public static string ExtractInlineText(
        string html)
    {
        if (string.IsNullOrWhiteSpace(
                html))
        {
            return string.Empty;
        }

        var value =
            RemoveNonContent(
                html);

        value =
            TagRegex.Replace(
                value,
                " ");

        value =
            WebUtility.HtmlDecode(
                value)
                .Replace(
                    '\u00A0',
                    ' ');

        return
            Regex.Replace(
                    value,
                    @"\s+",
                    " ")
                .Trim();
    }

    private static string RemoveNonContent(
        string value)
    {
        value =
            ScriptRegex.Replace(
                value,
                string.Empty);

        value =
            StyleRegex.Replace(
                value,
                string.Empty);

        value =
            NoScriptRegex.Replace(
                value,
                string.Empty);

        return
            CommentRegex.Replace(
                value,
                string.Empty);
    }
}
