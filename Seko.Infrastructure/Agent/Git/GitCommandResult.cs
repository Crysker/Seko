namespace Seko.Infrastructure.Agent.Git;

public sealed record GitCommandResult(
    int ExitCode,
    string Output)
{
    public bool Succeeded =>
        ExitCode == 0;
}
