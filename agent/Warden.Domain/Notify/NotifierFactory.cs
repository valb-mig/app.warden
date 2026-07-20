using Warden.Domain.Config;

namespace Warden.Domain.Notify;

/// <summary>Mirror de `notifier.py`'s `create_notifier`.</summary>
public static class NotifierFactory
{
    public static INotifier Create(GlobalConfig config) => config.NotifyChannel switch
    {
        "none" => new NullNotifier(),
        "ntfy" => new NtfyNotifier(
            config.NtfyTopic ?? throw new InvalidOperationException("notify_channel=\"ntfy\" exige ntfy_topic em config.toml"),
            config.NtfyServer),
        _ => throw new InvalidOperationException($"notify_channel desconhecido: \"{config.NotifyChannel}\""),
    };
}
