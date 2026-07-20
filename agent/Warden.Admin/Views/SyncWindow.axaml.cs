using Avalonia.Controls;
using Warden.Admin.Ipc;
using Warden.Admin.ViewModels;

namespace Warden.Admin.Views;

public partial class SyncWindow : Window
{
    private readonly SyncViewModel? _viewModel;

    public SyncWindow()
    {
        InitializeComponent();
    }

    public SyncWindow(AgentApiClient client) : this()
    {
        _viewModel = new SyncViewModel(client) { PickFolder = () => FolderBrowserDialog.PickAsync(this, client) };
        DataContext = _viewModel;
        Loaded += async (_, _) => await _viewModel.LoadAsync();
    }

    private void OnRemoveScanPathClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { Tag: string path })
        {
            _viewModel?.RemoveScanPathCommand.Execute(path);
        }
    }

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close(_viewModel?.AnyRegistered ?? false);

    public static async Task<bool> ShowAsync(Window owner, AgentApiClient client)
    {
        var window = new SyncWindow(client);
        return await window.ShowDialog<bool>(owner);
    }
}
