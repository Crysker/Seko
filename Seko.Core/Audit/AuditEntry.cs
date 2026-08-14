namespace Seko.Core.Audit;

public sealed class AuditEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;

    public required string Action { get; init; }

    public required string Description { get; init; }

    public bool Success { get; init; }
}