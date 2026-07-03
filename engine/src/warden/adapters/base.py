"""Interface comum de adapter: start/stop/status/logs (exec/ports/actions entram depois)."""

from abc import ABC, abstractmethod
from collections.abc import Callable
from dataclasses import dataclass, field


@dataclass
class ProcessStatus:
    running: bool
    pid: int | None = None
    ports: list[int] = field(default_factory=list)
    uptime_seconds: float | None = None
    cpu_percent: float | None = None
    memory_mb: float | None = None


class Adapter(ABC):
    @abstractmethod
    def start(self) -> None: ...

    @abstractmethod
    def stop(self) -> None: ...

    @abstractmethod
    def status(self) -> ProcessStatus: ...

    @abstractmethod
    def logs(self, tail: int = 100, service: str | None = None) -> list[str]: ...

    def set_on_exit(self, callback: Callable[[int], None]) -> None:  # noqa: B027
        """Chamado quando o processo termina sozinho (não via stop()). No-op por padrão."""

    def services(self) -> list[str]:
        """Serviços individuais (ex: containers de um compose). Vazio = processo único."""
        return []
