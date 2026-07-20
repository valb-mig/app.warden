using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Warden.Admin.Ipc;

namespace Warden.Admin.ViewModels;

/// <summary>
/// Raiz de navegação: troca `CurrentPage` entre Dashboard/ProjectDetail/Settings, sem router nem
/// pilha de histórico — a única navegação "pra trás" que existe é "Voltar pro dashboard", igual ao
/// `Link href="/"` do front Next.js. `RequestConfirm` é montado uma vez aqui (com referência real à
/// janela, vinda do code-behind) e repassado a cada página filha — mantém as ViewModels sem
/// dependência direta de `Window`/Avalonia.Controls.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private static readonly TimeSpan ToastDuration = TimeSpan.FromSeconds(4);

    private readonly AgentApiClient _client;
    private readonly Func<string, string, Task<bool>> _requestConfirm;
    private readonly Func<string, Task<bool>> _requestEditConfig;
    private DispatcherTimer? _toastTimer;

    [ObservableProperty]
    private ViewModelBase _currentPage;

    [ObservableProperty]
    private string? _toastMessage;

    [ObservableProperty]
    private bool _toastIsError;

    public DashboardViewModel Dashboard { get; }

    public SettingsViewModel Settings { get; }

    public ShellViewModel(
        AgentApiClient client,
        Func<string, string, Task<bool>> requestConfirm,
        Func<Task<bool>> requestSync,
        Func<string, Task<bool>> requestEditConfig)
    {
        _client = client;
        _requestConfirm = requestConfirm;
        _requestEditConfig = requestEditConfig;

        Dashboard = new DashboardViewModel(client, OpenProject) { RequestSync = requestSync };
        Settings = new SettingsViewModel(client) { RequestConfirm = requestConfirm };
        _currentPage = Dashboard;

        ToastService.Raised += OnToastRaised;
        Dashboard.OnActivated();
    }

    private void OnToastRaised(ToastMessage message)
    {
        ToastMessage = message.Text;
        ToastIsError = message.IsError;

        _toastTimer?.Stop();
        _toastTimer = new DispatcherTimer { Interval = ToastDuration };
        _toastTimer.Tick += (_, _) =>
        {
            ToastMessage = null;
            _toastTimer!.Stop();
        };
        _toastTimer.Start();
    }

    [RelayCommand]
    private void ShowDashboard() => Navigate(Dashboard);

    [RelayCommand]
    private void ShowSettings() => Navigate(Settings);

    private void OpenProject(string projectId, string displayName)
    {
        var detail = new ProjectDetailViewModel(_client, projectId, displayName, () => ShowDashboardCommand.Execute(null))
        {
            RequestConfirm = _requestConfirm,
            RequestEditConfig = _requestEditConfig,
        };
        Navigate(detail);
        detail.OnActivated();
    }

    private void Navigate(ViewModelBase page)
    {
        if (ReferenceEquals(CurrentPage, page)) return;
        CurrentPage.OnDeactivated();
        CurrentPage = page;
        page.OnActivated();
    }
}
