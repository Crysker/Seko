namespace Seko.Infrastructure.Agent.Web;

public sealed record WebFetchResult(
    string FinalUrl,
    string ContentType,
    string Title,
    string Text,
    bool Truncated);
