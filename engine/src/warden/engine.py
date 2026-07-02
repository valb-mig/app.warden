"""Engine — junta registry + adapters + event bus, mantém instância viva por projeto."""

import subprocess
from collections.abc import Callable
from dataclasses import dataclass
from pathlib import Path

from warden.adapters.base import Adapter, ProcessStatus
from warden.adapters.factory import create_adapter
from warden.bus import EventBus
from warden.config import ProjectConfig
from warden.events import Event, EventType
from warden.file_error_watcher import FileErrorWatcher
from warden.git import (
    CONFIRM_VERBS,
    GitCommandResult,
    GitInfo,
    git_command,
    git_info,
)
from warden.git_watcher import GitWatcher
from warden.languages import detect_languages
from warden.notifier import Notifier, NullNotifier
from warden.registry import Registry
from warden.store import EventStore


@dataclass
class ActionResult:
    exit_code: int
    output: str


class ConfirmationRequired(Exception):
    """Ação marcada `destructive = true` chamada sem `confirmed=True`."""


class Engine:
    def __init__(
        self,
        config_dir: Path,
        store: EventStore | None = None,
        notifier: Notifier | None = None,
    ):
        self.registry = Registry(config_dir)
        self.bus = EventBus()
        self.store = store
        self.notifier = notifier or NullNotifier()
        if store is not None:
            self.bus.subscribe(store.record)
        self.bus.subscribe(self._maybe_notify)
        self._adapters: dict[str, Adapter] = {}
        self._file_watchers: list[FileErrorWatcher] = []
        self._git_watchers: list[GitWatcher] = []

    def _maybe_notify(self, event: Event) -> None:
        try:
            project = self.registry.get(event.project_id)
        except KeyError:
            return
        if event.type == EventType.ERROR and project.notify.on_error:
            self.notifier.notify(event, project)
        elif event.type == EventType.FINISHED and project.notify.on_finished:
            self.notifier.notify(event, project)
        elif event.type == EventType.GIT_BEHIND and project.notify.on_git_behind:
            self.notifier.notify(event, project)

    def boot(self) -> None:
        self.registry.load()
        self._start_file_watchers()
        self._start_git_watchers()

    def shutdown(self) -> None:
        for watcher in self._file_watchers:
            watcher.stop()
        self._file_watchers.clear()
        for watcher in self._git_watchers:
            watcher.stop()
        self._git_watchers.clear()

    def _start_file_watchers(self) -> None:
        for project in self.registry.all():
            for log_source in project.log_sources:
                if log_source.type != "file" or not log_source.error_patterns:
                    continue
                watcher = FileErrorWatcher(
                    path=self._resolve_log_path(project, log_source.path),
                    patterns=log_source.error_patterns,
                    on_error=self._make_file_error_handler(project.id, log_source.name),
                )
                watcher.start()
                self._file_watchers.append(watcher)

    def _start_git_watchers(self) -> None:
        for project in self.registry.all():
            if not project.git.watch:
                continue
            watcher = GitWatcher(
                path=Path(project.path),
                remote=project.git.remote,
                interval=project.git.interval,
                on_behind=self._make_git_behind_handler(project.id, project.git.remote),
            )
            watcher.start()
            self._git_watchers.append(watcher)

    def _make_git_behind_handler(self, project_id: str, remote: str) -> Callable[[int], None]:
        def handler(behind: int) -> None:
            self.bus.publish(
                Event(
                    project_id=project_id,
                    type=EventType.GIT_BEHIND,
                    message=f"{behind} commit(s) atrás de {remote}",
                )
            )

        return handler

    @staticmethod
    def _resolve_log_path(project: ProjectConfig, log_path: str | None) -> Path:
        assert log_path is not None
        path = Path(log_path)
        return path if path.is_absolute() else Path(project.path) / path

    def _make_file_error_handler(
        self, project_id: str, source_name: str
    ) -> Callable[[str], None]:
        def handler(entry: str) -> None:
            self.bus.publish(
                Event(
                    project_id=project_id,
                    type=EventType.ERROR,
                    message=f"[{source_name}] {entry[:500]}",
                )
            )

        return handler

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

    def git_info(self, project_id: str) -> GitInfo | None:
        project = self.registry.get(project_id)
        return git_info(Path(project.path))

    def git_command(
        self, project_id: str, verb: str, confirmed: bool = False
    ) -> GitCommandResult:
        project = self.registry.get(project_id)
        if verb in CONFIRM_VERBS and not confirmed:
            raise ConfirmationRequired(f"verbo git {verb!r} exige confirmação")
        return git_command(Path(project.path), verb)

    def languages(self, project_id: str) -> list[str]:
        project = self.registry.get(project_id)
        return detect_languages(Path(project.path))

    def history(self, project_id: str, limit: int = 50) -> list[dict]:
        if self.store is None:
            return []
        return self.store.history(project_id, limit)

    def run_action(
        self, project_id: str, action_name: str, confirmed: bool = False
    ) -> ActionResult:
        project = self.registry.get(project_id)
        try:
            action = next(a for a in project.actions if a.name == action_name)
        except StopIteration:
            raise KeyError(f"ação {action_name!r} não encontrada em {project_id!r}") from None
        if action.interactive:
            raise ValueError(f"ação {action_name!r} é interativa, não suportada via API")
        if action.destructive and not confirmed:
            raise ConfirmationRequired(
                f"ação {action_name!r} é destrutiva — chame com confirmed=True"
            )
        result = subprocess.run(
            action.cmd, cwd=project.path, capture_output=True, text=True, timeout=300
        )
        if action.destructive and self.store is not None:
            self.store.record_action(
                project_id, action_name, action.cmd, confirmed, result.returncode
            )
        return ActionResult(exit_code=result.returncode, output=result.stdout + result.stderr)

    def action_audit(self, project_id: str, limit: int = 50) -> list[dict]:
        if self.store is None:
            return []
        return self.store.action_audit(project_id, limit)
