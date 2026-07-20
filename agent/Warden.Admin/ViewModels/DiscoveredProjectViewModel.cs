using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Warden.Admin.Ipc;
using Warden.Contracts.Discovery;

namespace Warden.Admin.ViewModels;

/// <summary>Um projeto encontrado em `scan_paths` ainda não registrado — "Registrar" roda preview (heurística do Scaffold) seguido de apply direto, sem edição manual (fica pra um follow-up: hoje já cobre o caso comum).</summary>
public sealed partial class DiscoveredProjectViewModel : ObservableObject
{
    private readonly AgentApiClient _client;

    public string Name { get; }
    public string Path { get; }
    public string Type { get; }

    public Action<DiscoveredProjectViewModel>? OnRegistered { get; set; }

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isRegistered;

    public DiscoveredProjectViewModel(AgentApiClient client, DiscoveredProjectDto dto)
    {
        _client = client;
        Name = dto.Name;
        Path = dto.Path;
        Type = dto.Type;
    }

    [RelayCommand(CanExecute = nameof(CanRegister))]
    private async Task RegisterAsync()
    {
        IsBusy = true;
        try
        {
            var preview = await _client.PreviewConfigAsync(Path);
            await _client.ApplyConfigAsync(preview.Config);
            IsRegistered = true;
            ToastService.Show($"{Name}: registrado");
            OnRegistered?.Invoke(this);
        }
        catch (AgentApiException ex)
        {
            ToastService.Show($"{Name}: falha ao registrar — {ex.Message}", isError: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanRegister() => !IsBusy && !IsRegistered;

    partial void OnIsBusyChanged(bool value) => RegisterCommand.NotifyCanExecuteChanged();

    partial void OnIsRegisteredChanged(bool value) => RegisterCommand.NotifyCanExecuteChanged();
}
