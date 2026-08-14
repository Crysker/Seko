namespace Seko.Infrastructure.Agent.Extensions;

public sealed record ExtensionInstallResult(
    string ExtensionId,
    string InstalledPath,
    string? BackupPath);
