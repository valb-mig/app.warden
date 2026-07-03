"""CPU/RAM por PID via psutil — cacheia o Process pra cpu_percent() dar delta real, não 0.0."""

import psutil


class VitalsSampler:
    def __init__(self) -> None:
        self._proc: psutil.Process | None = None
        self._pid: int | None = None

    def sample(self, pid: int) -> tuple[float, float] | None:
        if self._proc is None or self._pid != pid:
            try:
                self._proc = psutil.Process(pid)
            except psutil.NoSuchProcess:
                return None
            self._pid = pid
            self._proc.cpu_percent()  # prime: primeira leitura não tem baseline, descarta
            return None

        try:
            cpu = self._proc.cpu_percent()
            memory_mb = self._proc.memory_info().rss / (1024 * 1024)
        except psutil.NoSuchProcess:
            self._proc = None
            self._pid = None
            return None
        return cpu, memory_mb
