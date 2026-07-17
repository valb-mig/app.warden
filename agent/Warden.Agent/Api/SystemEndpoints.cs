using Warden.Domain;

namespace Warden.Agent.Api;

/// <summary>Rota de vitals da máquina como um todo — equivalente ao `system_routes.py` do FastAPI.</summary>
public static class SystemEndpoints
{
    public static RouteGroupBuilder MapSystemEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/vitals", (Engine engine) =>
        {
            var v = engine.SystemVitals();
            return Results.Ok(new SystemVitalsDto
            {
                CpuPercent = v.CpuPercent,
                MemoryPercent = v.MemoryPercent,
                MemoryUsedMb = v.MemoryUsedMb,
                MemoryTotalMb = v.MemoryTotalMb,
                DiskPercent = v.DiskPercent,
                DiskUsedGb = v.DiskUsedGb,
                DiskTotalGb = v.DiskTotalGb,
            });
        });

        return group;
    }
}
