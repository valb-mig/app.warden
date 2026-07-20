using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Warden.Admin.Ipc;
using Warden.Contracts.Projects;

namespace Warden.Admin.ViewModels;

/// <summary>Um evento de histórico já formatado pra exibição — espelha uma linha de `history-table.tsx`.</summary>
public sealed record HistoryRowViewModel(string When, string Type, string Message)
{
    public bool IsStarted => Type == "started";
    public bool IsStopped => Type == "stopped";
    public bool IsFinished => Type == "finished";
    public bool IsDanger => Type is "error" or "git_behind";

    public static HistoryRowViewModel From(HistoryEventDto dto)
    {
        var when = DateTimeOffset.TryParse(dto.CreatedAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToLocalTime().ToString("dd/MM HH:mm:ss", CultureInfo.InvariantCulture)
            : dto.CreatedAt;
        return new HistoryRowViewModel(when, dto.Type, string.IsNullOrEmpty(dto.Message) ? "—" : dto.Message);
    }
}

/// <summary>Espelha `history-table.tsx` — histórico de eventos started/stopped/finished/error/git_behind.</summary>
public sealed partial class HistoryViewModel : ObservableObject
{
    private readonly AgentApiClient _client;
    private readonly string _projectId;

    [ObservableProperty]
    private ObservableCollection<HistoryRowViewModel> _events = [];

    [ObservableProperty]
    private bool _failed;

    public bool HasEvents => Events.Count > 0;
    public bool ShowEmptyState => !Failed && !HasEvents;

    partial void OnEventsChanged(ObservableCollection<HistoryRowViewModel> value)
    {
        OnPropertyChanged(nameof(HasEvents));
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    partial void OnFailedChanged(bool value) => OnPropertyChanged(nameof(ShowEmptyState));

    public HistoryViewModel(AgentApiClient client, string projectId)
    {
        _client = client;
        _projectId = projectId;
    }

    public async Task RefreshAsync()
    {
        try
        {
            var history = await _client.GetHistoryAsync(_projectId, 20);
            Events = new ObservableCollection<HistoryRowViewModel>(history.Select(HistoryRowViewModel.From));
            Failed = false;
        }
        catch (AgentApiException)
        {
            Events = [];
            Failed = true;
        }
    }
}
