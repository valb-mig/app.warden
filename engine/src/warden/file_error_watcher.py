"""Tail de arquivo + regex -> evento de erro, sem tocar no projeto observado.

Cobre rotação de log (inode trocou / arquivo truncado) e stacktrace multi-linha
(agrupa linhas até a próxima entrada começar, ou até ficar 2s em silêncio —
cobre o erro fatal que é a última coisa escrita antes do processo morrer).
"""

import re
import threading
import time
from collections.abc import Callable
from pathlib import Path

_ENTRY_START_RE = re.compile(r"^\[")
_POLL_INTERVAL = 1.0
_IDLE_FLUSH_AFTER = 2.0
_MAX_ENTRY_LINES = 200


def _is_new_entry_start(line: str) -> bool:
    return bool(_ENTRY_START_RE.match(line))


class FileErrorWatcher:
    def __init__(self, path: Path, patterns: list[str], on_error: Callable[[str], None]):
        self.path = path
        self._patterns = [re.compile(p) for p in patterns]
        self._on_error = on_error
        self._stop_event = threading.Event()
        self._thread: threading.Thread | None = None
        self._inode: int | None = None
        self._pos = 0
        self._pending: list[str] | None = None
        self._last_line_at = 0.0

    def start(self) -> None:
        self._thread = threading.Thread(target=self._run, daemon=True)
        self._thread.start()

    def stop(self) -> None:
        self._stop_event.set()
        if self._thread is not None:
            self._thread.join(timeout=_POLL_INTERVAL * 2)

    def _run(self) -> None:
        while not self._stop_event.is_set():
            self._poll_once()
            self._stop_event.wait(_POLL_INTERVAL)

    def _poll_once(self) -> None:
        try:
            stat = self.path.stat()
        except OSError:
            return  # arquivo ainda não existe / sumiu momentaneamente durante rotação

        if self._inode is None:
            self._inode = stat.st_ino
            self._pos = stat.st_size  # começa no fim — não reprocessa histórico
            return

        if stat.st_ino != self._inode or stat.st_size < self._pos:
            self._inode = stat.st_ino
            self._pos = 0

        if stat.st_size == self._pos:
            if self._pending is not None and time.time() - self._last_line_at > _IDLE_FLUSH_AFTER:
                self._flush_pending()
            return

        with self.path.open("r", errors="replace") as f:
            f.seek(self._pos)
            new_text = f.read()
            self._pos = f.tell()

        for line in new_text.splitlines():
            self._feed_line(line)
        self._last_line_at = time.time()

    def _feed_line(self, line: str) -> None:
        if self._pending is not None and not _is_new_entry_start(line):
            self._pending.append(line)
            if len(self._pending) > _MAX_ENTRY_LINES:
                self._flush_pending()
            return
        if self._pending is not None:
            self._flush_pending()
        self._pending = [line]

    def _flush_pending(self) -> None:
        assert self._pending is not None
        entry = "\n".join(self._pending)
        self._pending = None
        if any(pattern.search(entry) for pattern in self._patterns):
            self._on_error(entry)
