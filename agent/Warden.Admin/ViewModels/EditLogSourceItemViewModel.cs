using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Warden.Contracts.Discovery;

namespace Warden.Admin.ViewModels;

/// <summary>Um log source no dialog de editar config — só permite remover, igual ao `project-config-modal.tsx` em modo edit.</summary>
public sealed partial class EditLogSourceItemViewModel(LogSourceDto source) : ObservableObject
{
    public LogSourceDto Source { get; } = source;

    public string Label => $"{Source.Name} ({Source.Type})";

    [ObservableProperty]
    private bool _removed;

    [RelayCommand]
    private void Toggle() => Removed = !Removed;
}
