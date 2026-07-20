using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Warden.Admin.Ipc;
using Warden.Contracts.Admin;

namespace Warden.Admin.ViewModels;

/// <summary>Config global do Agent (`~/.warden/config.toml`) — canal de notificação + pastas de descoberta.</summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    public static readonly IReadOnlyList<string> NotifyChannels = ["none", "ntfy"];

    private readonly AgentApiClient _client;

    public Func<string, string, Task<bool>>? RequestConfirm { get; set; }

    [ObservableProperty]
    private string _notifyChannel = "none";

    [ObservableProperty]
    private string _ntfyTopic = "";

    [ObservableProperty]
    private string _ntfyServer = "";

    [ObservableProperty]
    private string _scanPathsText = "";

    [ObservableProperty]
    private int _apiPort;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isSaving;

    public SettingsViewModel(AgentApiClient client)
    {
        _client = client;
    }

    public override void OnActivated() => _ = LoadAsync();

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var config = await _client.GetConfigAsync();
            Apply(config);
        }
        catch (AgentApiException ex)
        {
            ToastService.Show($"falha ao carregar config — {ex.Message}", isError: true);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        IsSaving = true;
        try
        {
            var scanPaths = ScanPathsText
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            var updated = new GlobalConfigDto
            {
                ApiPort = ApiPort,
                NotifyChannel = NotifyChannel,
                NtfyTopic = string.IsNullOrWhiteSpace(NtfyTopic) ? null : NtfyTopic,
                NtfyServer = NtfyServer,
                ScanPaths = scanPaths,
            };
            var saved = await _client.SaveConfigAsync(updated);
            Apply(saved);
            ToastService.Show("configuração salva");
        }
        catch (AgentApiException ex)
        {
            ToastService.Show($"falha ao salvar — {ex.Message}", isError: true);
        }
        finally
        {
            IsSaving = false;
        }
    }

    private void Apply(GlobalConfigDto config)
    {
        ApiPort = config.ApiPort;
        NotifyChannel = config.NotifyChannel;
        NtfyTopic = config.NtfyTopic ?? "";
        NtfyServer = config.NtfyServer;
        ScanPathsText = string.Join(Environment.NewLine, config.ScanPaths);
    }
}
