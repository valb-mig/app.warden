using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Warden.Admin.Ipc;
using Warden.Contracts.Projects;

namespace Warden.Admin.ViewModels;

/// <summary>Espelha `git-card.tsx`: branch/dirty/ahead-behind/último commit + fetch/sync/pull/push.</summary>
public sealed partial class GitCardViewModel : ObservableObject
{
    private readonly AgentApiClient _client;
    private readonly string _projectId;

    public Func<string, string, Task<bool>>? RequestConfirm { get; set; }

    [ObservableProperty]
    private GitInfoDto? _info;

    [ObservableProperty]
    private bool _isVisible;

    [ObservableProperty]
    private string? _pendingVerb;

    [ObservableProperty]
    private string? _resultTitle;

    [ObservableProperty]
    private string? _resultOutput;

    [ObservableProperty]
    private bool _resultOpen;

    public GitCardViewModel(AgentApiClient client, string projectId)
    {
        _client = client;
        _projectId = projectId;
    }

    public async Task RefreshAsync()
    {
        try
        {
            Info = await _client.GetGitInfoAsync(_projectId);
            IsVisible = Info is not null;
        }
        catch (AgentApiException)
        {
            IsVisible = false;
        }
    }

    [RelayCommand]
    private Task Sync() => RunAsync("sync", confirm: false, "Rodar git sync?", "fetch + fast-forward automático se limpo e atrás.");

    [RelayCommand]
    private Task Fetch() => RunAsync("fetch", confirm: false, "Rodar git fetch?", "Só atualiza o estado conhecido do remote, não muda arquivos.");

    [RelayCommand]
    private Task Pull() => RunAsync("pull", confirm: true, "Rodar git pull?", "Puxa commits do origin (fast-forward). Recusa se o working tree estiver sujo.");

    [RelayCommand]
    private Task Push() => RunAsync("push", confirm: true, "Rodar git push?", "Envia commits locais pro origin. Pode falhar se faltar credencial na máquina.");

    private async Task RunAsync(string verb, bool confirm, string title, string description)
    {
        if (confirm)
        {
            var ok = RequestConfirm is not null && await RequestConfirm(title, description);
            if (!ok) return;
        }

        PendingVerb = verb;
        try
        {
            var result = await _client.GitCommandAsync(_projectId, verb, confirm);
            if (result.Refused)
            {
                ToastService.Show(string.IsNullOrEmpty(result.Output) ? $"{verb}: bloqueado" : result.Output, isError: true);
            }
            else if (result.Ok)
            {
                ToastService.Show($"{verb}: {(string.IsNullOrEmpty(result.Output) ? "concluído" : result.Output)}");
            }
            else
            {
                ToastService.Show($"{verb}: saiu com código {result.ExitCode}", isError: true);
            }

            if (!string.IsNullOrEmpty(result.Output) && (!result.Ok || verb is "pull" or "push"))
            {
                ResultTitle = $"git {verb} — exit {result.ExitCode}";
                ResultOutput = result.Output;
                ResultOpen = true;
            }

            await RefreshAsync();
        }
        catch (AgentApiException ex)
        {
            ToastService.Show($"{verb}: falhou — {ex.Message}", isError: true);
        }
        finally
        {
            PendingVerb = null;
        }
    }

    [RelayCommand]
    private void CloseResult() => ResultOpen = false;
}
