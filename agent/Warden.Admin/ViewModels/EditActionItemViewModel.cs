using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Warden.Contracts.Discovery;

namespace Warden.Admin.ViewModels;

/// <summary>Uma action no dialog de editar config — permite remover e editar o comando, igual ao `project-config-modal.tsx` em modo edit.</summary>
public sealed partial class EditActionItemViewModel : ObservableObject
{
    public ActionConfigDto Original { get; }
    public string Name => Original.Name;

    [ObservableProperty]
    private bool _removed;

    [ObservableProperty]
    private string _cmdText;

    public EditActionItemViewModel(ActionConfigDto original)
    {
        Original = original;
        _cmdText = string.Join(' ', original.Cmd);
    }

    [RelayCommand]
    private void Toggle() => Removed = !Removed;
}
