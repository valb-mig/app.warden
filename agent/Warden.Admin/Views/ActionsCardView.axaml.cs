using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Warden.Admin.ViewModels;

namespace Warden.Admin.Views;

public partial class ActionsCardView : UserControl
{
    public ActionsCardView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is ActionsCardViewModel vm) vm.PropertyChanged += OnVmPropertyChanged;
        };
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ActionsCardViewModel.ResultOpen)) return;
        var vm = (ActionsCardViewModel)sender!;
        if (!vm.ResultOpen) return;

        var owner = this.FindAncestorOfType<Window>();
        if (owner is null) return;
        var dialog = new OutputDialog(vm.ResultTitle ?? "resultado", vm.ResultOutput ?? "");
        dialog.Closed += (_, _) => vm.CloseResultCommand.Execute(null);
        dialog.Show(owner);
    }
}
