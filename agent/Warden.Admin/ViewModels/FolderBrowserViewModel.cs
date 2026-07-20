using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Warden.Admin.Ipc;
using Warden.Contracts.Discovery;

namespace Warden.Admin.ViewModels;

/// <summary>Navegador de pasta simples — equivalente ao `folder-picker.tsx` do front, usando `/browse` (o browser não entrega path absoluto real de `<input type=file>` por segurança; quem tem acesso real ao filesystem é o Agent).</summary>
public sealed partial class FolderBrowserViewModel : ObservableObject
{
    private readonly AgentApiClient _client;

    [ObservableProperty]
    private string _currentPath = "";

    [ObservableProperty]
    private string? _parentPath;

    [ObservableProperty]
    private ObservableCollection<BrowseEntryDto> _entries = [];

    [ObservableProperty]
    private string? _errorMessage;

    public FolderBrowserViewModel(AgentApiClient client)
    {
        _client = client;
    }

    public async Task LoadAsync(string? path = null)
    {
        try
        {
            var result = await _client.BrowseAsync(path);
            CurrentPath = result.Path;
            ParentPath = result.Parent;
            Entries = new ObservableCollection<BrowseEntryDto>(result.Entries);
            ErrorMessage = null;
        }
        catch (AgentApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private Task NavigateUp() => ParentPath is null ? Task.CompletedTask : LoadAsync(ParentPath);

    [RelayCommand]
    private Task NavigateInto(BrowseEntryDto? entry) => entry is null ? Task.CompletedTask : LoadAsync(entry.Path);
}
