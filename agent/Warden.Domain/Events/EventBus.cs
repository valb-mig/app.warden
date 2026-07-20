namespace Warden.Domain.Events;

/// <summary>
/// Barramento de eventos interno — motor publica, `EventStore`/`Notifier` assinam. Mirror de
/// `bus.py`. Lock simples no publish/subscribe: eventos podem vir de threads diferentes (callback de
/// exit de processo, thread do `GitWatcher`, thread de request HTTP do start/stop), então a lista de
/// listeners precisa de proteção — Python não precisa disso por causa do GIL, C# precisa.
/// </summary>
public sealed class EventBus
{
    private readonly object _gate = new();
    private readonly List<Action<Event>> _listeners = [];

    public void Subscribe(Action<Event> listener)
    {
        lock (_gate) _listeners.Add(listener);
    }

    public void Publish(Event @event)
    {
        Action<Event>[] snapshot;
        lock (_gate) snapshot = [.. _listeners];
        foreach (var listener in snapshot) listener(@event);
    }
}
