using Seko.Infrastructure.Agent.Permissions;

namespace Seko.Infrastructure.Agent.Extensions;

public sealed record SekoExtensionPackage(
    SekoExtensionManifest Manifest,
    string RootPath,
    CapabilitySource Source);
