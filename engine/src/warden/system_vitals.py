"""CPU/RAM/disco da máquina como um todo — não por processo, isso é o vitals.py."""

from dataclasses import dataclass

import psutil


@dataclass
class SystemVitals:
    cpu_percent: float
    memory_percent: float
    memory_used_mb: float
    memory_total_mb: float
    disk_percent: float
    disk_used_gb: float
    disk_total_gb: float


def prime() -> None:
    """psutil.cpu_percent() sem baseline na primeira chamada — descarta o resultado."""
    psutil.cpu_percent()


def system_vitals() -> SystemVitals:
    mem = psutil.virtual_memory()
    disk = psutil.disk_usage("/")
    return SystemVitals(
        cpu_percent=psutil.cpu_percent(),
        memory_percent=mem.percent,
        memory_used_mb=mem.used / (1024 * 1024),
        memory_total_mb=mem.total / (1024 * 1024),
        disk_percent=disk.percent,
        disk_used_gb=disk.used / (1024**3),
        disk_total_gb=disk.total / (1024**3),
    )
