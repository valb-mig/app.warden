using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Warden.Admin.Ipc;

namespace Warden.Admin.ViewModels;

/// <summary>Espelha `actions-card.tsx`: lista de ações do manifesto aprovado, com confirmação + resultado.</summary>
public sealed partial class ActionsCardViewModel : ObservableObject
{
    private readonly AgentApiClient _client;
    private readonly string _projectId;

    public Func<string, string, Task<bool>>? RequestConfirm { get; set; }

    [ObservableProperty]
    private ObservableCollection<ActionItemViewModel> _actions = [];

    public bool HasActions => Actions.Count > 0;

    partial void OnActionsChanged(ObservableCollection<ActionItemViewModel> value) => OnPropertyChanged(nameof(HasActions));

    [ObservableProperty]
    private string? _resultTitle;

    [ObservableProperty]
    private string? _resultOutput;

    [ObservableProperty]
    private bool _resultOpen;

    public ActionsCardViewModel(AgentApiClient client, string projectId)
    {
        _client = client;
        _projectId = projectId;
    }

    public async Task RefreshAsync()
    {
        try
        {
            var items = await _client.ListActionsAsync(_projectId);
            Actions = new ObservableCollection<ActionItemViewModel>(items.Select(dto =>
            {
                var vm = new ActionItemViewModel(_client, _projectId, dto)
                {
                    RequestConfirm = RequestConfirm,
                    ShowResult = (title, output) =>
                    {
                        ResultTitle = title;
                        ResultOutput = output;
                        ResultOpen = true;
                    },
                };
                return vm;
            }));
        }
        catch (AgentApiException)
        {
            Actions = [];
        }
    }

    [RelayCommand]
    private void CloseResult() => ResultOpen = false;
}
