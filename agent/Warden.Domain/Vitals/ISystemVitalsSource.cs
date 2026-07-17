namespace Warden.Domain.Vitals;

/// <summary>
/// Leitura de CPU/RAM agregada do SO — a parte que exige P/Invoke ou parse de arquivo específico de
/// plataforma, atrás de uma interface (mesmo padrão de <see cref="Adapters.IPortDiscovery"/>).
/// </summary>
internal interface ISystemVitalsSource
{
    /// <summary>`psutil.cpu_percent()` sem baseline na primeira chamada retorna lixo — descarta a leitura inicial.</summary>
    void Prime();

    double SampleCpuPercent();

    (double UsedMb, double TotalMb) ReadMemory();
}
