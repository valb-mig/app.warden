using Warden.Domain.Vitals;
using Xunit;

namespace Warden.Domain.Tests;

/// <summary>
/// Sem mirror de teste Python direto (`system_vitals.py` não tem `test_system_vitals.py` — psutil
/// não é mockável facilmente ali também). Valida contra o SO real desta máquina (Linux): ranges
/// plausíveis e que o delta de CPU funciona entre duas leituras.
/// </summary>
public sealed class SystemVitalsSamplerTests
{
    [Fact]
    public void SampleReturnsPlausibleMemoryAndDiskValues()
    {
        var sampler = new SystemVitalsSampler();
        sampler.Prime();

        var vitals = sampler.Sample();

        Assert.InRange(vitals.MemoryPercent, 0.0, 100.0);
        Assert.True(vitals.MemoryTotalMb > 0);
        Assert.True(vitals.MemoryUsedMb >= 0);
        Assert.True(vitals.MemoryUsedMb <= vitals.MemoryTotalMb);

        Assert.InRange(vitals.DiskPercent, 0.0, 100.0);
        Assert.True(vitals.DiskTotalGb > 0);
        Assert.True(vitals.DiskUsedGb >= 0);
    }

    [Fact]
    public void SampleReturnsPlausibleCpuPercentAfterPriming()
    {
        var sampler = new SystemVitalsSampler();
        sampler.Prime();
        Thread.Sleep(150); // dá tempo real pra acumular jiffies/ticks entre as duas leituras

        var vitals = sampler.Sample();

        // A linha agregada "cpu" de /proc/stat já soma jiffies de todos os núcleos — percent aqui é
        // 0-100 (média da máquina inteira), diferente do `Adapters.VitalsSampler` por processo (que
        // pode passar de 100% com múltiplos núcleos ocupados).
        Assert.InRange(vitals.CpuPercent, 0.0, 100.0);
    }
}
