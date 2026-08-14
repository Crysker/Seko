namespace Seko.Infrastructure.Agent.Build;

public sealed record BuildResult(
    string? TargetPath,
    int ExitCode,
    string Output)
{
    public bool HasTarget =>
        !string.IsNullOrWhiteSpace(
            TargetPath);

    public bool Succeeded =>
        HasTarget
        && ExitCode == 0;
}
