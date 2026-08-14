namespace Seko.Infrastructure.Agent.Git;

public sealed record GitCommitResult(
    bool StagingSucceeded,
    bool HasChanges,
    bool CommitSucceeded,
    string Output,
    string CommitMessage,
    string ShortHash)
{
    public bool Succeeded =>
        StagingSucceeded
        && HasChanges
        && CommitSucceeded;
}
