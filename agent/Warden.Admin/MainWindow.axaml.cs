using Avalonia.Controls;
using Warden.Admin.Ipc;
using Warden.Admin.ViewModels;
using Warden.Admin.Views;

namespace Warden.Admin;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(AgentApiClient client) : this()
    {
        DataContext = new ShellViewModel(
            client,
            (title, message) => ConfirmDialog.ShowAsync(this, title, message),
            () => SyncWindow.ShowAsync(this, client),
            projectId => EditConfigDialog.EditAsync(this, client, projectId));
    }
}
