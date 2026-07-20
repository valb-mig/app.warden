namespace Warden.Domain.Events;

public sealed record HistoryEntry(string ProjectId, string Type, string Message, string CreatedAt);

public sealed record ActionAuditEntry(
    string ProjectId,
    string ActionName,
    IReadOnlyList<string> Cmd,
    bool Confirmed,
    int ExitCode,
    string CreatedAt);

/// <summary>Mirror de `store.py`'s `EventStore` — histórico estruturado (log bruto NÃO entra aqui, fica no ring buffer).</summary>
public interface IEventStore
{
    void Record(Event @event);

    IReadOnlyList<HistoryEntry> History(string projectId, int limit = 50);

    void RecordAction(string projectId, string actionName, IReadOnlyList<string> cmd, bool confirmed, int exitCode);

    IReadOnlyList<ActionAuditEntry> ActionAudit(string projectId, int limit = 50);
}
