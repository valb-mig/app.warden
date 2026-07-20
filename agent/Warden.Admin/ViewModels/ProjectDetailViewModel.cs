using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Warden.Admin.Ipc;
using Warden.Contracts.Projects;

namespace Warden.Admin.ViewModels;

/// <summary>Tela de detalhe de projeto — equivalente a `web/src/app/projects/[id]/page.tsx`.</summary>
public sealed partial class ProjectDetailViewModel : ViewModelBase
{
    private static readonly TimeSpan StatusPollInterval = TimeSpan.FromSeconds(3);

    private readonly AgentApiClient _client;
    private readonly Action _goBack;
    private DispatcherTimer? _statusTimer;

    public string ProjectId { get; }
    public string DisplayName { get; }

    public GitCardViewModel GitCard { get; }
    public ActionsCardViewModel ActionsCard { get; }
    public LogViewerViewModel LogViewer { get; }
    public HistoryViewModel History { get; }
    public VitalsChartViewModel Vitals { get; } = new();

    [ObservableProperty]
    private StatusDto? _status;

    [ObservableProperty]
    private bool _isPending;

    [ObservableProperty]
    private string _languagesText = "";

    private Func<string, string, Task<bool>>? _requestConfirm;

    public Func<string, string, Task<bool>>? RequestConfirm
    {
        get => _requestConfirm;
        set
        {
            _requestConfirm = value;
            GitCard.RequestConfirm = value;
            ActionsCard.RequestConfirm = value;
        }
    }

    /// <summary>Wireup vem do code-behind da janela (só ele tem uma `Window` real pra ser owner do dialog) — mesmo padrão do `RequestConfirm`.</summary>
    public Func<string, Task<bool>>? RequestEditConfig { get; set; }

    public bool Running => Status?.Running ?? false;
    public string RunningLabel => Status is null ? "..." : Running ? "rodando" : "parado";
    public string ToggleLabel => Running ? "Parar" : "Iniciar";
    public string PortsText => Status is { Ports.Count: > 0 } ? string.Join(", ", Status.Ports) : "—";
    public string UptimeText => FormatUptime(Status?.UptimeSeconds);
    public bool IsOutdated => Running && (GitCard.Info?.Behind ?? 0) > 0;

    public ProjectDetailViewModel(AgentApiClient client, string projectId, string displayName, Action goBack)
    {
        _client = client;
        ProjectId = projectId;
        DisplayName = displayName;
        _goBack = goBack;

        GitCard = new GitCardViewModel(client, projectId);
        ActionsCard = new ActionsCardViewModel(client, projectId);
        LogViewer = new LogViewerViewModel(client, projectId);
        History = new HistoryViewModel(client, projectId);

        GitCard.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GitCardViewModel.Info)) OnPropertyChanged(nameof(IsOutdated));
        };
    }

    public override void OnActivated()
    {
        _ = InitializeAsync();
        _statusTimer = new DispatcherTimer { Interval = StatusPollInterval };
        _statusTimer.Tick += async (_, _) => await PollStatusAsync();
        _statusTimer.Start();
    }

    public override void OnDeactivated()
    {
        _statusTimer?.Stop();
        _statusTimer = null;
        LogViewer.Stop();
    }

    /// <summary>
    /// Cada seção inicializa de forma independente (`Task.WhenAll`) — uma falha isolada numa (ex: git
    /// info de um projeto sem repo) não pode travar as outras silenciosamente, já que cada uma delas
    /// já se protege com try/catch por conta própria.
    /// </summary>
    private Task InitializeAsync() => Task.WhenAll(
        PollStatusAsync(),
        GitCard.RefreshAsync(),
        ActionsCard.RefreshAsync(),
        LogViewer.StartAsync(),
        History.RefreshAsync(),
        LoadLanguagesAsync());

    private async Task LoadLanguagesAsync()
    {
        try
        {
            var languages = await _client.GetLanguagesAsync(ProjectId);
            LanguagesText = string.Join(" · ", languages.Languages);
        }
        catch (AgentApiException)
        {
            LanguagesText = "";
        }
    }

    private async Task PollStatusAsync()
    {
        try
        {
            var s = await _client.GetStatusAsync(ProjectId);
            Status = s;
            if (s.Running && s.CpuPercent is { } cpu && s.MemoryMb is { } mem)
            {
                Vitals.AddSample(cpu, mem);
            }
        }
        catch (AgentApiException)
        {
            // falha isolada de polling — próximo tick tenta de novo
        }
    }

    [RelayCommand]
    private async Task ToggleAsync()
    {
        IsPending = true;
        try
        {
            if (Running)
            {
                await _client.StopAsync(ProjectId);
                ToastService.Show("stop disparado");
            }
            else
            {
                await _client.StartAsync(ProjectId);
                ToastService.Show("start disparado");
            }
            await PollStatusAsync();
        }
        catch (AgentApiException ex)
        {
            ToastService.Show($"ação falhou — {ex.Message}", isError: true);
        }
        finally
        {
            IsPending = false;
        }
    }

    [RelayCommand]
    private void Back() => _goBack();

    [RelayCommand]
    private async Task EditConfigAsync()
    {
        if (RequestEditConfig is null) return;
        var saved = await RequestEditConfig(ProjectId);
        if (!saved) return;

        ToastService.Show($"{ProjectId}: config salva");
        await ActionsCard.RefreshAsync();
    }

    partial void OnStatusChanged(StatusDto? value)
    {
        OnPropertyChanged(nameof(Running));
        OnPropertyChanged(nameof(RunningLabel));
        OnPropertyChanged(nameof(ToggleLabel));
        OnPropertyChanged(nameof(PortsText));
        OnPropertyChanged(nameof(UptimeText));
        OnPropertyChanged(nameof(IsOutdated));
    }

    private static string FormatUptime(double? seconds)
    {
        if (seconds is not { } s) return "—";
        var m = (int)(s / 60);
        var sec = (int)(s % 60);
        return m > 0 ? $"{m}m{sec}s" : $"{sec}s";
    }
}
