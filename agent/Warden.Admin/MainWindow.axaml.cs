using Avalonia.Controls;
using Avalonia.Layout;
using Warden.Admin.Ipc;
using Warden.Contracts.Admin;

namespace Warden.Admin;

public partial class MainWindow : Window
{
    private readonly AdminApiClient _client;

    public MainWindow()
    {
        InitializeComponent();

        var configDir = Environment.GetEnvironmentVariable("WARDEN_CONFIG_DIR")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".warden");
        var socketPath = AdminSocketPath.Resolve(configDir);
        _client = new AdminApiClient(socketPath);

        Loaded += async (_, _) =>
        {
            await RefreshProjectsAsync();
            await ReloadConfigAsync();
        };
    }

    private async void OnRefreshProjectsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await RefreshProjectsAsync();

    private async Task RefreshProjectsAsync()
    {
        ProjectsPanel.Children.Clear();
        ProjectsStatusText.Text = "carregando...";

        try
        {
            var projects = await _client.GetProjectsAsync();
            ProjectsStatusText.Text = projects.Count == 0 ? "nenhum projeto registrado" : "";
            foreach (var project in projects)
            {
                ProjectsPanel.Children.Add(BuildProjectRow(project));
            }
        }
        catch (Exception ex)
        {
            ProjectsStatusText.Text = $"não foi possível falar com o Agent: {ex.Message}";
        }
    }

    private Control BuildProjectRow(AdminProjectDto project)
    {
        var border = new Border
        {
            BorderBrush = Avalonia.Media.Brushes.Gray,
            BorderThickness = new Avalonia.Thickness(1),
            CornerRadius = new Avalonia.CornerRadius(4),
            Padding = new Avalonia.Thickness(10),
        };

        var stack = new StackPanel { Spacing = 4 };

        stack.Children.Add(new TextBlock
        {
            Text = $"{project.Name} ({project.Type}) — {project.Status}",
            FontWeight = Avalonia.Media.FontWeight.Bold,
        });

        if (project.Status == "PendingReview" && project.ApprovedCommands is not null)
        {
            var removed = project.ApprovedCommands.Except(project.Commands).ToList();
            var added = project.Commands.Except(project.ApprovedCommands).ToList();
            var diffParts = new List<string>();
            if (removed.Count > 0) diffParts.Add($"removido: {string.Join(", ", removed)}");
            if (added.Count > 0) diffParts.Add($"novo: {string.Join(", ", added)}");
            if (diffParts.Count > 0)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = string.Join(" | ", diffParts),
                    Foreground = Avalonia.Media.Brushes.OrangeRed,
                });
            }
        }
        else
        {
            stack.Children.Add(new TextBlock { Text = $"comandos: {string.Join(", ", project.Commands)}" });
        }

        if (project.Status != "Approved")
        {
            var approveButton = new Button { Content = "Aprovar", HorizontalAlignment = HorizontalAlignment.Left };
            approveButton.Click += async (_, _) =>
            {
                approveButton.IsEnabled = false;
                try
                {
                    await _client.ApproveAsync(project.Id);
                    await RefreshProjectsAsync();
                }
                catch (Exception ex)
                {
                    ProjectsStatusText.Text = $"falha ao aprovar {project.Id}: {ex.Message}";
                    approveButton.IsEnabled = true;
                }
            };
            stack.Children.Add(approveButton);
        }

        border.Child = stack;
        return border;
    }

    private async void OnReloadConfigClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await ReloadConfigAsync();

    private async Task ReloadConfigAsync()
    {
        ConfigStatusText.Text = "carregando...";
        try
        {
            var config = await _client.GetConfigAsync();
            NotifyChannelCombo.SelectedIndex = config.NotifyChannel == "ntfy" ? 1 : 0;
            NtfyTopicBox.Text = config.NtfyTopic;
            NtfyServerBox.Text = config.NtfyServer;
            ScanPathsBox.Text = string.Join(Environment.NewLine, config.ScanPaths);
            ConfigStatusText.Text = "";
        }
        catch (Exception ex)
        {
            ConfigStatusText.Text = $"não foi possível carregar config: {ex.Message}";
        }
    }

    private async void OnSaveConfigClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ConfigStatusText.Text = "salvando...";
        try
        {
            var current = await _client.GetConfigAsync();
            var scanPaths = (ScanPathsBox.Text ?? "")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            var updated = current with
            {
                NotifyChannel = ((ComboBoxItem)NotifyChannelCombo.SelectedItem!).Content!.ToString()!,
                NtfyTopic = string.IsNullOrWhiteSpace(NtfyTopicBox.Text) ? null : NtfyTopicBox.Text,
                NtfyServer = NtfyServerBox.Text ?? current.NtfyServer,
                ScanPaths = scanPaths,
            };

            await _client.SaveConfigAsync(updated);
            ConfigStatusText.Text = "salvo.";
        }
        catch (Exception ex)
        {
            ConfigStatusText.Text = $"falha ao salvar: {ex.Message}";
        }
    }
}
