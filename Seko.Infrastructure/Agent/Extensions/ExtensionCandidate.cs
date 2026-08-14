namespace Seko.Infrastructure.Agent.Extensions;

public sealed record ExtensionCandidate(
    string RootPath,
    SekoExtensionManifest Manifest);
