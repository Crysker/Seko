namespace Seko.Infrastructure.Agent.Extensions;

public sealed record SekoExtensionCatalog(
    IReadOnlyCollection<SekoExtensionPackage> Packages,
    IReadOnlyCollection<SekoExtensionLoadIssue> Issues);
