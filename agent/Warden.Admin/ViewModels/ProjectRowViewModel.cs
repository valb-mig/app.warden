using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Warden.Admin.Ipc;
using Warden.Contracts.Admin;
using Warden.Contracts.Projects;

namespace Warden.Admin.ViewModels;

/// <summary>Uma linha da tabela do dashboard — junta os 3 fetches por projeto (status/git/trust) num só objeto pra bind.</summary>
public sealed partial class ProjectRowViewModel : ObservableObject
{
    private readonly AgentApiClient _client;
    private readonly Action<string, string> _openProject;

    public string Id { get; }
    public string Name { get; }
    public string Type { get; }
    public string? Group { get; }

    [ObservableProperty]
    private StatusDto? _status;

    [ObservableProperty]
    private GitInfoDto? _git;

    [ObservableProperty]
    private string _trustStatus = "Approved";

    [ObservableProperty]
    private IReadOnlyList<string>? _approvedCommands;

    [ObservableProperty]
    private IReadOnlyList<string> _commands = [];

    [ObservableProperty]
    private bool _isBusy;

    public ProjectRowViewModel(AgentApiClient client, Action<string, string> openProject, ProjectDto dto)
    {
        _client = client;
        _openProject = openProject;
        Id = dto.Id;
        Name = dto.Name;
        Type = dto.Type;
        Group = dto.Group;
    }

    public bool Running => Status?.Running ?? false;
    public string RunningLabel => Status is null ? "..." : Running ? "rodando" : "parado";
    public string PortsText => Status is { Ports.Count: > 0 } ? string.Join(", ", Status.Ports) : "—";

    public bool HasGroup => !string.IsNullOrEmpty(Group);
    public bool NeedsApproval => TrustStatus != "Approved";
    public string TrustLabel => TrustStatus switch
    {
        "PendingReview" => "revisão pendente",
        "NeverApproved" => "nunca aprovado",
        _ => "aprovado",
    };

    public string GitLabel => Git switch
    {
        null => "—",
        { Dirty: true } g => $"sujo ({g.DirtyCount})",
        { Behind: > 0 } g => $"atrás ({g.Behind})",
        _ => "limpo",
    };

    public bool IsGitDirty => Git is { Dirty: true };
    public bool IsGitBehind => Git is { Dirty: false, Behind: > 0 };
    public bool IsGitClean => Git is { Dirty: false, Behind: null or <= 0 };
    public string ToggleLabel => Running ? "Parar" : "Iniciar";

    public void ApplyAdmin(AdminProjectDto dto)
    {
        TrustStatus = dto.Status;
        ApprovedCommands = dto.ApprovedCommands;
        Commands = dto.Commands;
    }

    partial void OnStatusChanged(StatusDto? value)
    {
        OnPropertyChanged(nameof(Running));
        OnPropertyChanged(nameof(RunningLabel));
        OnPropertyChanged(nameof(PortsText));
        OnPropertyChanged(nameof(ToggleLabel));
    }

    partial void OnGitChanged(GitInfoDto? value)
    {
        OnPropertyChanged(nameof(GitLabel));
        OnPropertyChanged(nameof(IsGitDirty));
        OnPropertyChanged(nameof(IsGitBehind));
        OnPropertyChanged(nameof(IsGitClean));
    }

    partial void OnTrustStatusChanged(string value)
    {
        OnPropertyChanged(nameof(NeedsApproval));
        OnPropertyChanged(nameof(TrustLabel));
        ToggleCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void Open() => _openProject(Id, Name);

    [RelayCommand(CanExecute = nameof(CanToggle))]
    private async Task ToggleAsync()
    {
        IsBusy = true;
        try
        {
            if (Running)
            {
                await _client.StopAsync(Id);
                ToastService.Show($"{Name}: stop disparado");
            }
            else
            {
                await _client.StartAsync(Id);
                ToastService.Show($"{Name}: start disparado");
            }
            Status = await _client.GetStatusAsync(Id);
        }
        catch (AgentApiException ex)
        {
            ToastService.Show($"{Name}: {ex.Message}", isError: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanToggle() => !IsBusy && Status is not null && !NeedsApproval;

    [RelayCommand]
    private async Task ApproveAsync()
    {
        IsBusy = true;
        try
        {
            var updated = await _client.ApproveAsync(Id);
            ApplyAdmin(updated);
            ToastService.Show($"{Name}: aprovado");
        }
        catch (AgentApiException ex)
        {
            ToastService.Show($"{Name}: falha ao aprovar — {ex.Message}", isError: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnIsBusyChanged(bool value) => ToggleCommand.NotifyCanExecuteChanged();
}
