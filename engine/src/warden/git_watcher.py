"""Fetch periódico -> detecta drift (commits novos no origin não puxados).

Só fetch (read-only, não mexe em working tree). Notifica uma vez na transição
pra "atrás do origin", não a cada poll — evita spam enquanto o usuário não agiu.
"""

import threading
from collections.abc import Callable
from pathlib import Path

from warden.git import git_command, git_info


class GitWatcher:
    def __init__(
        self,
        path: Path,
        remote: str,
        interval: float,
        on_behind: Callable[[int], None],
    ):
        self.path = path
        self._remote = remote
        self._interval = interval
        self._on_behind = on_behind
        self._stop_event = threading.Event()
        self._thread: threading.Thread | None = None
        self._last_behind = 0

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
        fetch = git_command(self.path, "fetch", remote=self._remote)
        if not fetch.ok:
            return  # rede instável / sem credencial — próximo poll cobre

        info = git_info(self.path)
        if info is None:
            return

        behind = info.behind or 0
        if behind > 0 and self._last_behind == 0:
            self._on_behind(behind)
        self._last_behind = behind
