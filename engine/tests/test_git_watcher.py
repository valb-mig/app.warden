import subprocess
import time
from pathlib import Path

from warden.git_watcher import GitWatcher


def _wait_until(predicate, timeout: float = 6.0) -> None:
    deadline = time.time() + timeout
    while time.time() < deadline:
        if predicate():
            return
        time.sleep(0.1)
    raise AssertionError("condição não satisfeita dentro do timeout")


def _git(path: Path, *args: str) -> None:
    subprocess.run(["git", "-C", str(path), *args], check=True, capture_output=True, text=True)


def _clone_pair(tmp_path: Path) -> tuple[Path, Path]:
    remote = tmp_path / "remote.git"
    remote.mkdir()
    _git(remote, "init", "--bare", "-b", "main")

    up = tmp_path / "up"
    up.mkdir()
    _git(up, "init", "-b", "main")
    _git(up, "config", "user.email", "up@warden.local")
    _git(up, "config", "user.name", "Up")
    (up / "a.txt").write_text("x")
    _git(up, "add", "a.txt")
    _git(up, "commit", "-m", "base")
    _git(up, "remote", "add", "origin", str(remote))
    _git(up, "push", "-u", "origin", "main")

    down = tmp_path / "down"
    subprocess.run(["git", "clone", str(remote), str(down)], check=True, capture_output=True)
    _git(down, "config", "user.email", "down@warden.local")
    _git(down, "config", "user.name", "Down")
    return up, down


def test_notifies_on_transition_to_behind(tmp_path: Path) -> None:
    up, down = _clone_pair(tmp_path)
    calls: list[int] = []
    watcher = GitWatcher(down, remote="origin", interval=0.2, on_behind=calls.append)
    watcher.start()
    try:
        (up / "b.txt").write_text("novo")
        _git(up, "add", "b.txt")
        _git(up, "commit", "-m", "novo commit")
        _git(up, "push", "origin", "main")

        _wait_until(lambda: calls == [1])
        time.sleep(0.6)  # continua atrás, mas não deve notificar de novo
        assert calls == [1]
    finally:
        watcher.stop()


def test_no_notification_when_up_to_date(tmp_path: Path) -> None:
    _up, down = _clone_pair(tmp_path)
    calls: list[int] = []
    watcher = GitWatcher(down, remote="origin", interval=0.2, on_behind=calls.append)
    watcher.start()
    try:
        time.sleep(0.6)
        assert calls == []
    finally:
        watcher.stop()


def test_non_repo_path_does_not_crash(tmp_path: Path) -> None:
    calls: list[int] = []
    watcher = GitWatcher(tmp_path, remote="origin", interval=0.2, on_behind=calls.append)
    watcher.start()
    try:
        time.sleep(0.5)
        assert calls == []
    finally:
        watcher.stop()
