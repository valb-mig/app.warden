using Avalonia.Controls;
using Avalonia.Interactivity;
using Warden.Admin.Ipc;
using Warden.Admin.ViewModels;

namespace Warden.Admin.Views;

public partial class EditConfigDialog : Window
{
    private readonly EditConfigViewModel? _viewModel;

    public EditConfigDialog()
    {
        InitializeComponent();
    }

    public EditConfigDialog(AgentApiClient client, string projectId) : this()
    {
        _viewModel = new EditConfigViewModel(client, projectId);
        DataContext = _viewModel;
        Loaded += async (_, _) => await _viewModel.LoadAsync();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        var saved = await _viewModel.SaveAsync();
        if (saved) Close(true);
    }

    public static async Task<bool> EditAsync(Window owner, AgentApiClient client, string projectId)
    {
        var dialog = new EditConfigDialog(client, projectId);
        return await dialog.ShowDialog<bool>(owner);
    }
}
