namespace Warden.Domain.Events;

/// <summary>Mirror de `events.py`'s `EventType` (StrEnum) — os valores viram string minúscula na persistência/API (ver <see cref="EventTypeExtensions.ToWireString"/>).</summary>
public enum EventType
{
    Started,
    Stopped,
    Finished,
    Error,
    GitBehind,
    CronAction,
}

public static class EventTypeExtensions
{
    public static string ToWireString(this EventType type) => type switch
    {
        EventType.Started => "started",
        EventType.Stopped => "stopped",
        EventType.Finished => "finished",
        EventType.Error => "error",
        EventType.GitBehind => "git_behind",
        EventType.CronAction => "cron_action",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    public static EventType FromWireString(string value) => value switch
    {
        "started" => EventType.Started,
        "stopped" => EventType.Stopped,
        "finished" => EventType.Finished,
        "error" => EventType.Error,
        "git_behind" => EventType.GitBehind,
        "cron_action" => EventType.CronAction,
        _ => throw new ArgumentException($"tipo de evento desconhecido: {value}", nameof(value)),
    };
}
