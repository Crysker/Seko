using Seko.Core.Audit;

namespace Seko.Infrastructure.Audit;

public sealed class InMemoryAuditLog : IAuditLog
{
    private readonly List<AuditEntry> _entries = new();

    public IReadOnlyList<AuditEntry> Entries => _entries;

    public void Add(AuditEntry entry)
    {
        _entries.Add(entry);
    }
}
