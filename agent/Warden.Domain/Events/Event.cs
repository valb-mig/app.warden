namespace Warden.Domain.Events;

/// <summary>Mirror de `events.py`'s `Event`.</summary>
public sealed record Event(string ProjectId, EventType Type, string Message = "");
