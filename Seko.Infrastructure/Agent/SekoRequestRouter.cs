namespace Seko.Infrastructure.Agent;

public sealed record SekoRequestRoutingDecision(
    bool UseFastConversation,
    TaskIntent TaskIntent,
    bool RequiresWebResearch);

public static class SekoRequestRouter
{
    private const int MaximumFastConversationRequestCharacters =
        700;

    private static readonly string[] DeliberateAgentPhrases =
    {
        "deep dive",
        "in depth",
        "in-depth",
        "comprehensive analysis",
        "detailed analysis",
        "thorough analysis",
        "analyze this thoroughly",
        "reason carefully",
        "take your time",
        "detailed plan",
        "step-by-step plan",
        "multi-step plan",
        "develop a strategy",
        "architecture plan",
        "design a system"
    };

    public static SekoRequestRoutingDecision Route(
        string request)
    {
        request ??=
            string.Empty;

        var taskIntent =
            TaskIntentAnalyzer.Analyze(
                request);

        var requiresWebResearch =
            WebResearchIntentDetector.RequiresWebResearch(
                request);

        var useFastConversation =
            !taskIntent.RequiresWorkspaceTools
            && !requiresWebResearch
            && IsSimpleConversation(
                request);

        return
            new SekoRequestRoutingDecision(
                useFastConversation,
                taskIntent,
                requiresWebResearch);
    }

    private static bool IsSimpleConversation(
        string request)
    {
        var trimmed =
            request.Trim();

        if (trimmed.Length
            > MaximumFastConversationRequestCharacters)
        {
            return false;
        }

        var normalized =
            trimmed.ToLowerInvariant();

        return
            !DeliberateAgentPhrases.Any(
                normalized.Contains);
    }
}
