using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Warden.Admin.Ipc;
using Warden.Contracts.Admin;

namespace Warden.Admin.ViewModels;

/// <summary>Tela inicial — equivalente a `web/src/app/page.tsx`: tabela de projetos, busca, banner de atenção e vitals da máquina.</summary>
public sealed partial class DashboardViewModel : ViewModelBase
{
    private static readonly TimeSpan StatusPollInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan GitPollInterval = TimeSpan.FromSeconds(15);

    private readonly AgentApiClient _client;
    private readonly Action<string, string> _openProject;
    private readonly HashSet<string> _dismissed = [];
    private readonly string _dismissedFile;

    private DispatcherTimer? _statusTimer;
    private DispatcherTimer? _gitTimer;

    [ObservableProperty]
    private ObservableCollection<ProjectRowViewModel> _allProjects = [];

    [ObservableProperty]
    private ObservableCollection<ProjectRowViewModel> _filteredProjects = [];

    [ObservableProperty]
    private ObservableCollection<AttentionItemViewModel> _attentionItems = [];

    [ObservableProperty]
    private string _searchQuery = "";

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private string? _errorMessage;

    public SystemVitalsViewModel SystemVitals { get; }

    /// <summary>Wireup vem do code-behind da janela (só ele tem uma `Window` real pra ser owner do SyncWindow) — mesmo padrão do `RequestConfirm`.</summary>
    public Func<Task<bool>>? RequestSync { get; set; }

    public int RunningCount => AllProjects.Count(p => p.Running);
    public bool HasProjects => FilteredProjects.Count > 0;
    public bool HasAttentionItems => AttentionItems.Count > 0;
    public bool HasErrorMessage => !string.IsNullOrEmpty(ErrorMessage);

    public DashboardViewModel(AgentApiClient client, Action<string, string> openProject)
    {
        _client = client;
        _openProject = openProject;
        SystemVitals = new SystemVitalsViewModel(client);

        var configDir = Environment.GetEnvironmentVariable("WARDEN_CONFIG_DIR")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".warden");
        _dismissedFile = Path.Combine(configDir, "admin_dismissed.json");
        LoadDismissed();
    }

    public override void OnActivated()
    {
        SystemVitals.OnActivated();
        _ = ReloadAsync();

        _statusTimer = new DispatcherTimer { Interval = StatusPollInterval };
        _statusTimer.Tick += async (_, _) => await PollStatusAsync();
        _statusTimer.Start();

        _gitTimer = new DispatcherTimer { Interval = GitPollInterval };
        _gitTimer.Tick += async (_, _) => await PollGitAndTrustAsync();
        _gitTimer.Start();
    }

    public override void OnDeactivated()
    {
        SystemVitals.OnDeactivated();
        _statusTimer?.Stop();
        _statusTimer = null;
        _gitTimer?.Stop();
        _gitTimer = null;
    }

    [RelayCommand]
    public async Task ReloadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var projects = await _client.ListProjectsAsync();
            var rows = projects.Select(p => new ProjectRowViewModel(_client, _openProject, p)).ToList();
            AllProjects = new ObservableCollection<ProjectRowViewModel>(rows);
            ApplyFilter();
            await PollStatusAsync();
            await PollGitAndTrustAsync();
        }
        catch (AgentApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnFilteredProjectsChanged(ObservableCollection<ProjectRowViewModel> value) => OnPropertyChanged(nameof(HasProjects));

    partial void OnAttentionItemsChanged(ObservableCollection<AttentionItemViewModel> value) => OnPropertyChanged(nameof(HasAttentionItems));

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasErrorMessage));

    [RelayCommand]
    private async Task OpenSyncAsync()
    {
        if (RequestSync is null) return;
        var registeredSomething = await RequestSync();
        if (registeredSomething) await ReloadAsync();
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var q = SearchQuery.Trim().ToLowerInvariant();
        IEnumerable<ProjectRowViewModel> query = AllProjects;
        if (q.Length > 0)
        {
            query = query.Where(p =>
                p.Name.ToLowerInvariant().Contains(q) ||
                p.Id.ToLowerInvariant().Contains(q) ||
                p.Type.ToLowerInvariant().Contains(q) ||
                (p.Group ?? "").ToLowerInvariant().Contains(q));
        }
        var sorted = query.OrderBy(p => p.Group ?? "").ThenBy(p => p.Name);
        FilteredProjects = new ObservableCollection<ProjectRowViewModel>(sorted);
    }

    private async Task PollStatusAsync()
    {
        foreach (var row in AllProjects.ToList())
        {
            try
            {
                row.Status = await _client.GetStatusAsync(row.Id);
            }
            catch (AgentApiException)
            {
                // isolado — próximo tick tenta de novo, mesma tolerância do polling no front
            }
        }
        OnPropertyChanged(nameof(RunningCount));
    }

    private async Task PollGitAndTrustAsync()
    {
        IReadOnlyList<AdminProjectDto> adminProjects;
        try
        {
            adminProjects = await _client.GetAdminProjectsAsync();
        }
        catch (AgentApiException)
        {
            adminProjects = [];
        }
        var adminById = adminProjects.ToDictionary(p => p.Id);

        foreach (var row in AllProjects.ToList())
        {
            if (adminById.TryGetValue(row.Id, out var admin)) row.ApplyAdmin(admin);
            try
            {
                row.Git = await _client.GetGitInfoAsync(row.Id);
            }
            catch (AgentApiException)
            {
                // sem git ou falha isolada — mantém último valor conhecido
            }
        }

        RecomputeAttention();
    }

    private void RecomputeAttention()
    {
        var items = new List<AttentionItemViewModel>();
        foreach (var row in AllProjects)
        {
            if (row.NeedsApproval)
            {
                AddAttention(items, $"{row.Id}-approval-{row.TrustStatus}", row.Id,
                    $"{row.Name}: {row.TrustLabel} — precisa de aprovação");
            }
            if (row.Git is { Dirty: true } dirty)
            {
                AddAttention(items, $"{row.Id}-dirty-{dirty.DirtyCount}", row.Id,
                    $"{row.Name}: {dirty.DirtyCount} arquivo(s) não commitado(s)");
            }
            var behind = row.Git?.Behind ?? 0;
            if (behind > 0)
            {
                AddAttention(items, $"{row.Id}-behind-{behind}", row.Id,
                    $"{row.Name}: {behind} commit(s) atrás do origin");
            }
        }
        AttentionItems = new ObservableCollection<AttentionItemViewModel>(items);
    }

    private void AddAttention(List<AttentionItemViewModel> items, string key, string projectId, string label)
    {
        if (_dismissed.Contains(key)) return;
        items.Add(new AttentionItemViewModel(key, projectId, label, OpenProjectById, DismissAttention));
    }

    private void OpenProjectById(string projectId)
    {
        var row = AllProjects.FirstOrDefault(p => p.Id == projectId);
        _openProject(projectId, row?.Name ?? projectId);
    }

    private void DismissAttention(string key)
    {
        _dismissed.Add(key);
        SaveDismissed();
        AttentionItems = new ObservableCollection<AttentionItemViewModel>(AttentionItems.Where(i => i.Key != key));
    }

    private void LoadDismissed()
    {
        try
        {
            if (!File.Exists(_dismissedFile)) return;
            var keys = JsonSerializer.Deserialize<string[]>(File.ReadAllText(_dismissedFile)) ?? [];
            foreach (var key in keys) _dismissed.Add(key);
        }
        catch (IOException)
        {
        }
        catch (JsonException)
        {
        }
    }

    private void SaveDismissed()
    {
        try
        {
            File.WriteAllText(_dismissedFile, JsonSerializer.Serialize(_dismissed));
        }
        catch (IOException)
        {
        }
    }
}
