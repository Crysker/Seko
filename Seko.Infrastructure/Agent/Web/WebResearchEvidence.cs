namespace Seko.Infrastructure.Agent.Web;

public sealed record WebResearchEvidence(
    WebSearchResult SearchResult,
    WebFetchResult? Page,
    string? Error);
