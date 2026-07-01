"""Engine — junta registry + adapters + event bus, mantém instância viva por projeto."""

import subprocess
from dataclasses import dataclass
from pathlib import Path

from warden.adapters.base import Adapter, ProcessStatus
from warden.adapters.factory import create_adapter
from warden.bus import EventBus
from warden.events import Event, EventType
from warden.registry import Registry
from warden.store import EventStore


@dataclass
class ActionResult:
    exit_code: int
    output: str


class Engine:
    def __init__(self, config_dir: Path, store: EventStore | None = None):
        self.registry = Registry(config_dir)
        self.bus = EventBus()
        self.store = store
        if store is not None:
            self.bus.subscribe(store.record)
        self._adapters: dict[str, Adapter] = {}

    def boot(self) -> None:
        self.registry.load()

    def _adapter(self, project_id: str) -> Adapter:
        if project_id not in self._adapters:
            config = self.registry.get(project_id)
            adapter = create_adapter(config)
            adapter.set_on_exit(lambda returncode: self._handle_exit(project_id, returncode))
            self._adapters[project_id] = adapter
        return self._adapters[project_id]

    def _handle_exit(self, project_id: str, returncode: int) -> None:
        event_type = EventType.FINISHED if returncode == 0 else EventType.ERROR
        self.bus.publish(
            Event(project_id=project_id, type=event_type, message=f"exit={returncode}")
        )

    def start(self, project_id: str) -> None:
        self._adapter(project_id).start()
        self.bus.publish(Event(project_id=project_id, type=EventType.STARTED))

    def stop(self, project_id: str) -> None:
        self._adapter(project_id).stop()
        self.bus.publish(Event(project_id=project_id, type=EventType.STOPPED))

    def status(self, project_id: str) -> ProcessStatus:
        return self._adapter(project_id).status()

    def logs(self, project_id: str, tail: int = 100, service: str | None = None) -> list[str]:
        return self._adapter(project_id).logs(tail, service=service)

    def services(self, project_id: str) -> list[str]:
        return self._adapter(project_id).services()

    def history(self, project_id: str, limit: int = 50) -> list[dict]:
        if self.store is None:
            return []
        return self.store.history(project_id, limit)

    def run_action(self, project_id: str, action_name: str) -> ActionResult:
        project = self.registry.get(project_id)
        try:
            action = next(a for a in project.actions if a.name == action_name)
        except StopIteration:
            raise KeyError(f"ação {action_name!r} não encontrada em {project_id!r}") from None
        if action.interactive:
            raise ValueError(f"ação {action_name!r} é interativa, não suportada via API")
        result = subprocess.run(
            action.cmd, cwd=project.path, capture_output=True, text=True, timeout=300
        )
        return ActionResult(exit_code=result.returncode, output=result.stdout + result.stderr)
