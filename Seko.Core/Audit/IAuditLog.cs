namespace Seko.Core.Audit;

public interface IAuditLog
{
    IReadOnlyList<AuditEntry> Entries { get; }

    void Add(AuditEntry entry);
}