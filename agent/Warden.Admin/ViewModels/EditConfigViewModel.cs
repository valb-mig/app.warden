using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using Warden.Admin.Ipc;
using Warden.Contracts.Discovery;

namespace Warden.Admin.ViewModels;

/// <summary>
/// Editar config de um projeto já registrado — equivalente ao modo `edit` do `project-config-modal.tsx`
/// do front. Só cobre o mesmo escopo dele: id somente-leitura, grupo, comando de start, toggle-remover
/// log sources/actions e editar comando de action. Sem preview de TOML (o front também não mostra em
/// modo edit) e sem criar projeto novo (isso já é coberto pelo fluxo de "Sincronizar").
/// </summary>
public sealed partial class EditConfigViewModel : ObservableObject
{
    private readonly AgentApiClient _client;
    private readonly string _projectId;
    private ProjectConfigDto? _original;

    [ObservableProperty]
    private bool _loading = true;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string _typeLabel = "";

    [ObservableProperty]
    private string _pathLabel = "";

    [ObservableProperty]
    private string _group = "";

    [ObservableProperty]
    private bool _hasStart;

    [ObservableProperty]
    private string _startCmdText = "";

    [ObservableProperty]
    private ObservableCollection<EditLogSourceItemViewModel> _logSources = [];

    [ObservableProperty]
    private ObservableCollection<EditActionItemViewModel> _actions = [];

    public string ProjectId => _projectId;
    public bool HasLogSources => LogSources.Count > 0;
    public bool HasActions => Actions.Count > 0;

    public EditConfigViewModel(AgentApiClient client, string projectId)
    {
        _client = client;
        _projectId = projectId;
    }

    public async Task LoadAsync()
    {
        Loading = true;
        try
        {
            _original = await _client.GetProjectConfigAsync(_projectId);
            TypeLabel = _original.Type;
            PathLabel = _original.Path;
            Group = _original.Group ?? "";
            HasStart = _original.Start is not null;
            StartCmdText = _original.Start is null ? "" : string.Join(' ', _original.Start.Cmd);
            LogSources = new ObservableCollection<EditLogSourceItemViewModel>(
                _original.LogSources.Select(s => new EditLogSourceItemViewModel(s)));
            Actions = new ObservableCollection<EditActionItemViewModel>(
                _original.Actions.Select(a => new EditActionItemViewModel(a)));
            ErrorMessage = null;
        }
        catch (AgentApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            Loading = false;
        }
    }

    /// <summary>Retorna true se salvou com sucesso.</summary>
    public async Task<bool> SaveAsync()
    {
        if (_original is null) return false;

        var updated = _original with
        {
            Group = string.IsNullOrWhiteSpace(Group) ? null : Group,
            Start = _original.Start is null ? null : _original.Start with { Cmd = SplitCommand(StartCmdText) },
            LogSources = LogSources.Where(s => !s.Removed).Select(s => s.Source).ToList(),
            Actions = Actions.Where(a => !a.Removed)
                .Select(a => a.Original with { Cmd = SplitCommand(a.CmdText) })
                .ToList(),
        };

        try
        {
            await _client.ApplyConfigAsync(updated);
            ErrorMessage = null;
            return true;
        }
        catch (AgentApiException ex)
        {
            ErrorMessage = ex.Message;
            return false;
        }
    }

    partial void OnLogSourcesChanged(ObservableCollection<EditLogSourceItemViewModel> value) => OnPropertyChanged(nameof(HasLogSources));

    partial void OnActionsChanged(ObservableCollection<EditActionItemViewModel> value) => OnPropertyChanged(nameof(HasActions));

    /// <summary>Mesma regra de `splitCommand` do `project-config-modal.tsx` — respeita aspas duplas, split por espaço fora delas.</summary>
    private static List<string> SplitCommand(string input) =>
        Regex.Matches(input, """(?:[^\s"]+|"[^"]*")+""")
            .Select(m => m.Value.Trim('"'))
            .ToList();
}
