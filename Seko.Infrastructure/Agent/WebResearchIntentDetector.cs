namespace Seko.Infrastructure.Agent;

public static class WebResearchIntentDetector
{
    private static readonly string[] ExplicitWebPhrases =
    {
        "search the web",
        "search web",
        "web search",
        "search online",
        "look online",
        "look it up online",
        "look this up online",
        "browse the web",
        "browse online",
        "on the internet",
        "from the internet",
        "website",
        "webpage",
        "web page",
        "url",
        "http://",
        "https://",
        "research this",
        "research online",
        "find sources",
        "with sources",
        "cite sources",
        "citations"
    };

    private static readonly string[] FreshnessPhrases =
    {
        "latest",
        "currently",
        "current news",
        "recent news",
        "breaking news",
        "news about",
        "today",
        "this week",
        "this month",
        "current price",
        "who is the current",
        "what is the current",
        "what's the current",
        "current status of",
        "current version",
        "latest price",
        "price today",
        "current release",
        "latest release",
        "latest version",
        "current version of",
        "weather",
        "flight",
        "hotel",
        "travel",
        "restaurant",
        "product comparison",
        "compare products"
    };

    private static readonly string[] StrongWorkspacePhrases =
    {
        "seko",
        "workspace",
        "codebase",
        "repository",
        " repo ",
        " project ",
        " file ",
        "source code",
        "your code",
        "your source",
        "your ui",
        "your task",
        "task log",
        ".cs",
        ".xaml",
        ".csproj",
        ".sln"
    };

    public static bool RequiresWebResearch(
        string request)
    {
        if (string.IsNullOrWhiteSpace(
                request))
        {
            return false;
        }

        var normalized =
            " "
            + request
                .Trim()
                .ToLowerInvariant()
            + " ";

        if (ExplicitWebPhrases.Any(
                normalized.Contains))
        {
            return true;
        }

        var hasFreshnessSignal =
            FreshnessPhrases.Any(
                normalized.Contains);

        if (!hasFreshnessSignal)
        {
            return false;
        }

        var isStronglyWorkspaceScoped =
            StrongWorkspacePhrases.Any(
                normalized.Contains);

        return
            !isStronglyWorkspaceScoped;
    }
}
