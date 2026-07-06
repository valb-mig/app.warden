"""Poll de ~/.warden/projects/*.toml -> recarrega registry quando algo muda.

Sem isso, criar/editar/remover um .toml com o servidor já rodando só aparecia
depois de reiniciar (`Registry.load()` só rodava no boot). Poll de mtime é
simples o bastante pra não precisar de inotify/watchdog como dependência
nova — mesmo padrão do `GitWatcher`/`FileErrorWatcher` (thread própria).
"""

import threading
from collections.abc import Callable
from pathlib import Path

DEFAULT_INTERVAL = 2.0


def _snapshot(projects_dir: Path) -> frozenset[tuple[str, float]]:
    if not projects_dir.exists():
        return frozenset()
    return frozenset((f.name, f.stat().st_mtime) for f in projects_dir.glob("*.toml"))


class ProjectsWatcher:
    def __init__(
        self,
        projects_dir: Path,
        on_change: Callable[[], None],
        interval: float = DEFAULT_INTERVAL,
    ):
        self.projects_dir = projects_dir
        self._on_change = on_change
        self._interval = interval
        self._stop_event = threading.Event()
        self._thread: threading.Thread | None = None
        self._last_snapshot = _snapshot(projects_dir)

    def start(self) -> None:
        self._thread = threading.Thread(target=self._run, daemon=True)
        self._thread.start()

    def stop(self) -> None:
        self._stop_event.set()
        if self._thread is not None:
            self._thread.join(timeout=2.0)

    def _run(self) -> None:
        while not self._stop_event.is_set():
            self._poll_once()
            self._stop_event.wait(self._interval)

    def _poll_once(self) -> None:
        current = _snapshot(self.projects_dir)
        if current != self._last_snapshot:
            self._last_snapshot = current
            self._on_change()
