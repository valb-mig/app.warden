using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using Warden.Admin.Ipc;
using Warden.Contracts.Admin;

namespace Warden.Admin;

public partial class App : Application
{
    private static readonly TimeSpan StatusPollInterval = TimeSpan.FromSeconds(5);

    private MainWindow? _mainWindow;
    private TrayIcon? _trayIcon;
    private AgentApiClient? _statusClient;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var configDir = Environment.GetEnvironmentVariable("WARDEN_CONFIG_DIR")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".warden");
            var socketPath = AdminSocketPath.Resolve(configDir);
            var client = new AgentApiClient(socketPath, configDir);

            _mainWindow = new MainWindow(client);
            desktop.MainWindow = _mainWindow;
            SetupTrayIcon(desktop);
            StartStatusPolling(configDir);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void SetupTrayIcon(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var icon = new WindowIcon(AssetLoader.Open(new Uri("avares://Warden.Admin/Assets/tray-icon.ico")));

        var openItem = new NativeMenuItem("Abrir");
        openItem.Click += (_, _) => ShowMainWindow();

        var exitItem = new NativeMenuItem("Sair");
        exitItem.Click += (_, _) => desktop.Shutdown();

        _trayIcon = new TrayIcon
        {
            Icon = icon,
            ToolTipText = "Warden Admin",
            Menu = new NativeMenu { Items = { openItem, exitItem } },
        };
        _trayIcon.Clicked += (_, _) => ShowMainWindow();

        TrayIcon.SetIcons(this, [_trayIcon]);
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null) return;
        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    private void StartStatusPolling(string configDir)
    {
        _statusClient = new AgentApiClient(AdminSocketPath.Resolve(configDir), configDir);

        var timer = new DispatcherTimer { Interval = StatusPollInterval };
        timer.Tick += async (_, _) =>
        {
            var up = await _statusClient.IsAgentUpAsync();
            if (_trayIcon is not null)
            {
                _trayIcon.ToolTipText = up ? "Warden Admin — Agent rodando" : "Warden Admin — Agent parado";
            }
        };
        timer.Start();
    }
}
