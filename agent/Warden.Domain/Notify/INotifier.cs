using Warden.Domain.Config;
using Warden.Domain.Events;

namespace Warden.Domain.Notify;

/// <summary>Strategy plugável de canal de notificação — motor só dispara evento, canal decide depois. Mirror de `notifier.py`.</summary>
public interface INotifier
{
    void Notify(Event @event, ProjectConfig project);
}

/// <summary>Default quando nenhum canal está configurado.</summary>
public sealed class NullNotifier : INotifier
{
    public void Notify(Event @event, ProjectConfig project)
    {
    }
}
