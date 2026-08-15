namespace Seko.Infrastructure.Agent.Web;

public sealed record WebResearchPacket(
    string Query,
    int SearchResultCount,
    IReadOnlyCollection<WebResearchEvidence> Sources);
