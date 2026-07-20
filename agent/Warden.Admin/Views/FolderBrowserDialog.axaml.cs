using Avalonia.Controls;
using Avalonia.Input;
using Warden.Admin.Ipc;
using Warden.Admin.ViewModels;
using Warden.Contracts.Discovery;

namespace Warden.Admin.Views;

public partial class FolderBrowserDialog : Window
{
    private readonly FolderBrowserViewModel? _viewModel;

    public FolderBrowserDialog()
    {
        InitializeComponent();
    }

    public FolderBrowserDialog(AgentApiClient client, string? startPath) : this()
    {
        _viewModel = new FolderBrowserViewModel(client);
        DataContext = _viewModel;
        Loaded += async (_, _) => await _viewModel.LoadAsync(startPath);
    }

    private void OnEntryDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_viewModel is null) return;
        if (EntriesList.SelectedItem is BrowseEntryDto entry)
        {
            _viewModel.NavigateIntoCommand.Execute(entry);
        }
    }

    private void OnCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close(null);

    private void OnChooseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close(_viewModel?.CurrentPath);

    public static async Task<string?> PickAsync(Window owner, AgentApiClient client, string? startPath = null)
    {
        var dialog = new FolderBrowserDialog(client, startPath);
        return await dialog.ShowDialog<string?>(owner);
    }
}
