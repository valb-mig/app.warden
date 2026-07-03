from fastapi import APIRouter, Depends

from warden.api.deps import get_engine
from warden.api.schemas import SystemVitalsOut
from warden.engine import Engine

router = APIRouter(tags=["system"])


@router.get("/system/vitals", response_model=SystemVitalsOut)
def get_system_vitals(engine: Engine = Depends(get_engine)) -> SystemVitalsOut:
    v = engine.system_vitals()
    return SystemVitalsOut(
        cpu_percent=v.cpu_percent,
        memory_percent=v.memory_percent,
        memory_used_mb=v.memory_used_mb,
        memory_total_mb=v.memory_total_mb,
        disk_percent=v.disk_percent,
        disk_used_gb=v.disk_used_gb,
        disk_total_gb=v.disk_total_gb,
    )
