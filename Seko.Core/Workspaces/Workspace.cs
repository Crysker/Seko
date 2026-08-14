namespace Seko.Core.Workspaces;

public sealed class Workspace
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required string RootPath { get; init; }
}