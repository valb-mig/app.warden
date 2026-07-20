using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Warden.Admin.Ipc;

namespace Warden.Admin.ViewModels;

/// <summary>Card "Máquina" do topo do dashboard — espelha `system-vitals-card.tsx`.</summary>
public sealed partial class SystemVitalsViewModel : ViewModelBase
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);
    private readonly AgentApiClient _client;
    private DispatcherTimer? _timer;

    [ObservableProperty]
    private double _cpuPercent;

    [ObservableProperty]
    private double _memoryPercent;

    [ObservableProperty]
    private string _memoryDetail = "";

    [ObservableProperty]
    private double _diskPercent;

    [ObservableProperty]
    private string _diskDetail = "";

    [ObservableProperty]
    private bool _hasData;

    public SystemVitalsViewModel(AgentApiClient client)
    {
        _client = client;
    }

    public override void OnActivated()
    {
        _ = PollOnceAsync();
        _timer = new DispatcherTimer { Interval = PollInterval };
        _timer.Tick += async (_, _) => await PollOnceAsync();
        _timer.Start();
    }

    public override void OnDeactivated()
    {
        _timer?.Stop();
        _timer = null;
    }

    private async Task PollOnceAsync()
    {
        try
        {
            var v = await _client.GetSystemVitalsAsync();
            CpuPercent = v.CpuPercent;
            MemoryPercent = v.MemoryPercent;
            MemoryDetail = $"{v.MemoryUsedMb:F0} / {v.MemoryTotalMb:F0} MB";
            DiskPercent = v.DiskPercent;
            DiskDetail = $"{v.DiskUsedGb:F1} / {v.DiskTotalGb:F1} GB";
            HasData = true;
        }
        catch (AgentApiException)
        {
            // silencioso — mesma tolerância do polling no front (próxima tentativa cobre falha isolada)
        }
    }
}
