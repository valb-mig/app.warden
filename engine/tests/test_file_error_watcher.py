import time
from pathlib import Path

import pytest

from warden.file_error_watcher import FileErrorWatcher


def _wait_until(predicate, timeout: float = 6.0) -> None:
    deadline = time.time() + timeout
    while time.time() < deadline:
        if predicate():
            return
        time.sleep(0.1)
    raise AssertionError("condição não satisfeita dentro do timeout")


@pytest.fixture
def log_file(tmp_path: Path) -> Path:
    path = tmp_path / "app.log"
    path.write_text("")
    return path


def _watcher(path: Path, patterns: list[str], calls: list[str]) -> FileErrorWatcher:
    return FileErrorWatcher(path, patterns, calls.append)


def test_single_line_match_flushes_by_idle(log_file: Path) -> None:
    calls: list[str] = []
    watcher = _watcher(log_file, ["ERROR"], calls)
    watcher.start()
    try:
        with log_file.open("a") as f:
            f.write("[2024-01-01 00:00:00] production.ERROR: something broke\n")
        _wait_until(lambda: len(calls) == 1)
        assert "something broke" in calls[0]
    finally:
        watcher.stop()


def test_no_match_never_calls_back(log_file: Path) -> None:
    calls: list[str] = []
    watcher = _watcher(log_file, ["ERROR"], calls)
    watcher.start()
    try:
        with log_file.open("a") as f:
            f.write("[2024-01-01 00:00:00] production.INFO: all good\n")
        time.sleep(3.0)
        assert calls == []
    finally:
        watcher.stop()


def test_multiline_stacktrace_grouped_as_one_entry(log_file: Path) -> None:
    calls: list[str] = []
    watcher = _watcher(log_file, [r"\bException\b"], calls)
    watcher.start()
    try:
        with log_file.open("a") as f:
            f.write("[2024-01-01 00:00:00] production.ERROR: boom\n")
            f.write("Stack trace:\n")
            f.write("#0 Exception thrown here\n")
        _wait_until(lambda: len(calls) == 1)
        assert "Exception" in calls[0]
        assert calls[0].count("\n") == 2  # 3 linhas viraram 1 entrada só
    finally:
        watcher.stop()


def test_new_entry_start_flushes_previous_pending(log_file: Path) -> None:
    calls: list[str] = []
    watcher = _watcher(log_file, ["ERROR"], calls)
    watcher.start()
    try:
        with log_file.open("a") as f:
            f.write("[2024-01-01 00:00:00] production.ERROR: first\n")
            f.write("continuation of first\n")
        time.sleep(0.5)  # bem menor que o idle threshold (2s) — ainda não deve ter flushado
        assert calls == []

        with log_file.open("a") as f:
            f.write("[2024-01-01 00:00:01] production.INFO: second, sem match\n")
        _wait_until(lambda: len(calls) == 1)
        assert "first" in calls[0]
        assert "continuation of first" in calls[0]
    finally:
        watcher.stop()


def test_rotation_by_new_inode(log_file: Path) -> None:
    calls: list[str] = []
    watcher = _watcher(log_file, ["ERROR"], calls)
    watcher.start()
    try:
        time.sleep(1.2)  # garante baseline (inode/pos) capturado antes de rotacionar
        log_file.unlink()
        log_file.write_text("[2024-01-01 00:00:00] production.ERROR: after rotation\n")
        _wait_until(lambda: len(calls) == 1)
        assert "after rotation" in calls[0]
    finally:
        watcher.stop()


def test_truncation_same_inode_copytruncate(log_file: Path) -> None:
    calls: list[str] = []
    watcher = _watcher(log_file, ["ERROR"], calls)
    watcher.start()
    try:
        time.sleep(1.2)  # baseline capturado
        with log_file.open("a") as f:
            # padding grande o bastante pra ficar maior que o conteúdo pós-truncate —
            # detecção de truncamento é por tamanho (novo st_size < pos conhecido),
            # que é o caso real do copytruncate (arquivo volta pequeno após rotação)
            f.write("padding line here\n" * 20)
        time.sleep(1.2)  # padding processado, pos avançou
        with log_file.open("w") as f:
            f.write("[2024-01-01 00:00:00] production.ERROR: after truncate\n")
        _wait_until(lambda: len(calls) == 1)
        assert "after truncate" in calls[0]
    finally:
        watcher.stop()


def test_missing_file_does_not_crash(tmp_path: Path) -> None:
    calls: list[str] = []
    watcher = _watcher(tmp_path / "does-not-exist.log", ["ERROR"], calls)
    watcher.start()
    try:
        time.sleep(1.5)
        assert calls == []
    finally:
        watcher.stop()
