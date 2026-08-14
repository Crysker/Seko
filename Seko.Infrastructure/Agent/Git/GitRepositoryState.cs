namespace Seko.Infrastructure.Agent.Git;

public sealed record GitRepositoryState(
    bool IsRepository,
    bool IsClean);
