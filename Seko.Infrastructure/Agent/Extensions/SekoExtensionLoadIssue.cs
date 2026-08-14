namespace Seko.Infrastructure.Agent.Extensions;

public sealed record SekoExtensionLoadIssue(
    string Path,
    string Message);
