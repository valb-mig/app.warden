using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Warden.Admin.Ipc;

namespace Warden.Admin.ViewModels;

/// <summary>
/// Conteúdo do diálogo "Sincronizar" — gerência de `scan_paths` + descoberta/registro de projeto
/// novo, equivalente ao "Sincronizar" do header do front Next.js (decisão #17 do TODO.md).
/// </summary>
public sealed partial class SyncViewModel : ObservableObject
{
    private readonly AgentApiClient _client;

    /// <summary>Wireup vem do code-behind da janela (só ele tem uma `Window` real pra ser owner do FolderBrowserDialog).</summary>
    public Func<Task<string?>>? PickFolder { get; set; }

    public bool AnyRegistered { get; private set; }

    [ObservableProperty]
    private ObservableCollection<string> _scanPaths = [];

    [ObservableProperty]
    private ObservableCollection<DiscoveredProjectViewModel> _discovered = [];

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    public bool HasScanPaths => ScanPaths.Count > 0;
    public bool HasDiscovered => Discovered.Count > 0;

    partial void OnScanPathsChanged(ObservableCollection<string> value) => OnPropertyChanged(nameof(HasScanPaths));

    partial void OnDiscoveredChanged(ObservableCollection<DiscoveredProjectViewModel> value) => OnPropertyChanged(nameof(HasDiscovered));

    public SyncViewModel(AgentApiClient client)
    {
        _client = client;
    }

    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var scanPaths = await _client.GetScanPathsAsync();
            ScanPaths = new ObservableCollection<string>(scanPaths.ScanPaths);
            await RefreshDiscoveredAsync();
            ErrorMessage = null;
        }
        catch (AgentApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RefreshDiscoveredAsync()
    {
        try
        {
            var result = await _client.DiscoverAsync();
            Discovered = new ObservableCollection<DiscoveredProjectViewModel>(result.Projects.Select(p =>
            {
                var vm = new DiscoveredProjectViewModel(_client, p);
                vm.OnRegistered = _ => AnyRegistered = true;
                return vm;
            }));
        }
        catch (AgentApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task AddScanPathAsync()
    {
        if (PickFolder is null) return;
        var path = await PickFolder();
        if (path is null) return;

        try
        {
            var updated = await _client.AddScanPathAsync(path);
            ScanPaths = new ObservableCollection<string>(updated.ScanPaths);
            await RefreshDiscoveredAsync();
        }
        catch (AgentApiException ex)
        {
            ToastService.Show($"falha ao adicionar pasta — {ex.Message}", isError: true);
        }
    }

    [RelayCommand]
    private async Task RemoveScanPathAsync(string? path)
    {
        if (path is null) return;
        try
        {
            var updated = await _client.RemoveScanPathAsync(path);
            ScanPaths = new ObservableCollection<string>(updated.ScanPaths);
            await RefreshDiscoveredAsync();
        }
        catch (AgentApiException ex)
        {
            ToastService.Show($"falha ao remover pasta — {ex.Message}", isError: true);
        }
    }
}
