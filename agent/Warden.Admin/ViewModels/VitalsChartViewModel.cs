using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Warden.Admin.ViewModels;

/// <summary>Espelha as sparklines de `vitals-card.tsx` — acumula amostras cpu/mem em memória (sem persistência, mesma decisão do front) e expõe pontos prontos pra um `Polyline`.</summary>
public sealed partial class VitalsChartViewModel : ObservableObject
{
    private const int MaxSamples = 100;
    private const double Width = 300;
    private const double Height = 56;
    private const double Padding = 6;

    private readonly List<double> _cpu = [];
    private readonly List<double> _mem = [];

    [ObservableProperty]
    private Points _cpuPoints = [];

    [ObservableProperty]
    private Points _memPoints = [];

    [ObservableProperty]
    private double _currentCpu;

    [ObservableProperty]
    private double _currentMem;

    [ObservableProperty]
    private bool _hasEnoughData;

    public void AddSample(double cpuPercent, double memoryMb)
    {
        _cpu.Add(cpuPercent);
        _mem.Add(memoryMb);
        if (_cpu.Count > MaxSamples)
        {
            _cpu.RemoveAt(0);
            _mem.RemoveAt(0);
        }

        CurrentCpu = cpuPercent;
        CurrentMem = memoryMb;
        HasEnoughData = _cpu.Count >= 2;
        if (!HasEnoughData) return;

        CpuPoints = BuildPoints(_cpu);
        MemPoints = BuildPoints(_mem);
    }

    private static Points BuildPoints(IReadOnlyList<double> values)
    {
        var min = values.Min();
        var max = values.Max();
        var span = max - min == 0 ? 1 : max - min;
        var step = Width / (values.Count - 1);

        var points = new Points();
        for (var i = 0; i < values.Count; i++)
        {
            var x = i * step;
            var y = Height - Padding - (values[i] - min) / span * (Height - Padding * 2);
            points.Add(new Point(x, y));
        }
        return points;
    }
}
