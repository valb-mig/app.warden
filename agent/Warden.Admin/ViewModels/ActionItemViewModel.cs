using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Warden.Admin.Ipc;
using Warden.Contracts.Projects;

namespace Warden.Admin.ViewModels;

/// <summary>Um botão do card de ações — espelha o item de `actions-card.tsx`.</summary>
public sealed partial class ActionItemViewModel : ObservableObject
{
    private readonly AgentApiClient _client;
    private readonly string _projectId;

    public string Name { get; }
    public bool Interactive { get; }
    public bool Destructive { get; }
    public bool Approved { get; }

    public Func<string, string, Task<bool>>? RequestConfirm { get; set; }
    public Action<string, string>? ShowResult { get; set; }

    [ObservableProperty]
    private bool _isBusy;

    public ActionItemViewModel(AgentApiClient client, string projectId, ActionDto dto)
    {
        _client = client;
        _projectId = projectId;
        Name = dto.Name;
        Interactive = dto.Interactive;
        Destructive = dto.Destructive;
        Approved = dto.Approved;
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        var confirmed = RequestConfirm is null || await RequestConfirm(
            $"Rodar {Name}?",
            "Executa o comando configurado no projeto — pode alterar dados (ex: migration, seed).");
        if (!confirmed) return;

        IsBusy = true;
        try
        {
            var result = await _client.RunActionAsync(_projectId, Name);
            ShowResult?.Invoke($"{Name} — exit {result.ExitCode}", string.IsNullOrEmpty(result.Output) ? "(sem saída)" : result.Output);
            ToastService.Show(result.ExitCode == 0 ? $"{Name}: concluído" : $"{Name}: saiu com código {result.ExitCode}", isError: result.ExitCode != 0);
        }
        catch (AgentApiException ex)
        {
            ToastService.Show($"{Name}: ação falhou — {ex.Message}", isError: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanRun() => !IsBusy && !Interactive && Approved;

    partial void OnIsBusyChanged(bool value) => RunCommand.NotifyCanExecuteChanged();
}
