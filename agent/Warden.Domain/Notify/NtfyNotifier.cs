using Warden.Domain.Config;
using Warden.Domain.Events;

namespace Warden.Domain.Notify;

/// <summary>Mirror de `notifier.py`'s `NtfyNotifier` — POST simples pro `ntfy.sh` (ou self-hosted), canal secundário: falha de rede não derruba o motor.</summary>
public sealed class NtfyNotifier : INotifier
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    public string Topic { get; }
    public string Server { get; }

    public NtfyNotifier(string topic, string server = "https://ntfy.sh")
    {
        Topic = topic;
        Server = server.TrimEnd('/');
    }

    public void Notify(Event @event, ProjectConfig project)
    {
        var title = $"{project.DisplayName}: {@event.Type.ToWireString()}";
        var body = string.IsNullOrEmpty(@event.Message) ? @event.Type.ToWireString() : @event.Message;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{Server}/{Topic}")
            {
                Content = new StringContent(body),
            };
            request.Headers.TryAddWithoutValidation("Title", title);
            using var response = Http.Send(request);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // canal secundário — falha de rede não derruba o motor
        }
    }
}
