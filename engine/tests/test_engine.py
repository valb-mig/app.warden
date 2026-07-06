import subprocess
import sys
import time
from pathlib import Path

import pytest

from warden.engine import ConfirmationRequired, Engine
from warden.events import Event, EventType
from warden.store import EventStore


def _wait_until(predicate, timeout: float = 3.0) -> None:
    deadline = time.time() + timeout
    while time.time() < deadline:
        if predicate():
            return
        time.sleep(0.05)
    raise AssertionError("condição não satisfeita dentro do timeout")


def _write_project(
    config_dir: Path, project_id: str, cmd: list[str], notify_on_error: bool = False
) -> None:
    notify = f"\n[notify]\non_error = {str(notify_on_error).lower()}\n" if notify_on_error else ""
    projects_dir = config_dir / "projects"
    projects_dir.mkdir(parents=True, exist_ok=True)
    (projects_dir / f"{project_id}.toml").write_text(
        f'id = "{project_id}"\ntype = "raw"\npath = "{config_dir}"\n\n[start]\ncmd = {cmd!r}\n'
        + notify
    )


class _FakeNotifier:
    def __init__(self) -> None:
        self.calls: list[Event] = []

    def notify(self, event: Event, project) -> None:
        self.calls.append(event)


def _engine_with_store(tmp_path: Path) -> tuple[Engine, list]:
    store = EventStore(tmp_path / "warden.db")
    engine = Engine(tmp_path, store=store)
    engine.boot()
    events: list = []
    engine.bus.subscribe(events.append)
    return engine, events


def test_start_and_stop_emit_events_without_finished(tmp_path: Path) -> None:
    _write_project(tmp_path, "demo", [sys.executable, "-c", "import time; time.sleep(30)"])
    engine, events = _engine_with_store(tmp_path)

    engine.start("demo")
    _wait_until(lambda: engine.status("demo").running is True)
    engine.stop("demo")
    _wait_until(lambda: engine.status("demo").running is False)

    time.sleep(0.3)  # dá tempo pra watcher errado disparar, se houver bug
    types = [e.type for e in events]
    assert types == [EventType.STARTED, EventType.STOPPED]


def test_process_finishing_on_its_own_emits_finished(tmp_path: Path) -> None:
    _write_project(tmp_path, "demo", [sys.executable, "-c", "pass"])
    engine, events = _engine_with_store(tmp_path)

    engine.start("demo")
    _wait_until(lambda: any(e.type == EventType.FINISHED for e in events))

    assert [e.type for e in events] == [EventType.STARTED, EventType.FINISHED]
    history = engine.history("demo")
    assert history[0]["type"] == "finished"


def test_process_erroring_on_its_own_emits_error(tmp_path: Path) -> None:
    _write_project(tmp_path, "demo", [sys.executable, "-c", "import sys; sys.exit(1)"])
    engine, events = _engine_with_store(tmp_path)

    engine.start("demo")
    _wait_until(lambda: any(e.type == EventType.ERROR for e in events))

    assert [e.type for e in events] == [EventType.STARTED, EventType.ERROR]


def test_notifier_called_only_when_project_opts_in(tmp_path: Path) -> None:
    _write_project(
        tmp_path,
        "notify-on",
        [sys.executable, "-c", "import sys; sys.exit(1)"],
        notify_on_error=True,
    )
    _write_project(tmp_path, "notify-off", [sys.executable, "-c", "import sys; sys.exit(1)"])
    notifier = _FakeNotifier()
    engine = Engine(tmp_path, notifier=notifier)
    engine.boot()

    engine.start("notify-on")
    engine.start("notify-off")
    _wait_until(lambda: len(notifier.calls) >= 1)
    time.sleep(0.3)

    assert [e.project_id for e in notifier.calls] == ["notify-on"]


def test_boot_starts_file_error_watcher_and_emits_error_event(tmp_path: Path) -> None:
    log_dir = tmp_path / "logs"
    log_dir.mkdir()
    log_file = log_dir / "app.log"
    log_file.write_text("")
    projects_dir = tmp_path / "projects"
    projects_dir.mkdir()
    (projects_dir / "demo.toml").write_text(
        f'id = "demo"\ntype = "raw"\npath = "{tmp_path}"\n\n'
        '[[log_sources]]\nname = "app"\ntype = "file"\npath = "./logs/app.log"\n'
        'error_patterns = ["ERROR"]\n'
    )
    engine = Engine(tmp_path)
    engine.boot()
    events: list = []
    engine.bus.subscribe(events.append)

    try:
        with log_file.open("a") as f:
            f.write("[2024-01-01 00:00:00] production.ERROR: boom\n")
        _wait_until(lambda: any(e.type == EventType.ERROR for e in events), timeout=6.0)
        assert "boom" in events[0].message
    finally:
        engine.shutdown()


def _git(path: Path, *args: str) -> None:
    subprocess.run(["git", "-C", str(path), *args], check=True, capture_output=True, text=True)


def test_boot_starts_git_watcher_and_emits_git_behind_event(tmp_path: Path) -> None:
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

    projects_dir = tmp_path / "projects"
    projects_dir.mkdir()
    (projects_dir / "demo.toml").write_text(
        f'id = "demo"\ntype = "raw"\npath = "{down}"\n\n'
        '[start]\ncmd = ["true"]\n\n'
        "[git]\nwatch = true\ninterval = 0.2\n"
    )

    notifier = _FakeNotifier()
    engine = Engine(tmp_path, notifier=notifier)
    engine.boot()
    events: list = []
    engine.bus.subscribe(events.append)

    try:
        (up / "b.txt").write_text("novo")
        _git(up, "add", "b.txt")
        _git(up, "commit", "-m", "novo commit")
        _git(up, "push", "origin", "main")

        _wait_until(lambda: any(e.type == EventType.GIT_BEHIND for e in events), timeout=6.0)
        assert notifier.calls == []  # notify.on_git_behind não foi ligado neste projeto
    finally:
        engine.shutdown()


def _write_project_with_action(config_dir: Path, project_id: str, action_toml: str) -> None:
    projects_dir = config_dir / "projects"
    projects_dir.mkdir(parents=True, exist_ok=True)
    (projects_dir / f"{project_id}.toml").write_text(
        f'id = "{project_id}"\ntype = "raw"\npath = "{config_dir}"\n\n'
        f'[start]\ncmd = ["true"]\n\n{action_toml}'
    )


def test_destructive_action_without_confirmation_raises(tmp_path: Path) -> None:
    _write_project_with_action(
        tmp_path,
        "demo",
        '[[actions]]\nname = "wipe"\ncmd = ["true"]\ndestructive = true\n',
    )
    engine = Engine(tmp_path)
    engine.boot()

    with pytest.raises(ConfirmationRequired):
        engine.run_action("demo", "wipe")


def test_destructive_action_confirmed_runs_and_audits(tmp_path: Path) -> None:
    _write_project_with_action(
        tmp_path,
        "demo",
        '[[actions]]\nname = "wipe"\ncmd = ["echo", "wiped"]\ndestructive = true\n',
    )
    store = EventStore(tmp_path / "warden.db")
    engine = Engine(tmp_path, store=store)
    engine.boot()

    result = engine.run_action("demo", "wipe", confirmed=True)

    assert result.exit_code == 0
    audit = engine.action_audit("demo")
    assert len(audit) == 1
    assert audit[0]["action_name"] == "wipe"
    assert audit[0]["confirmed"] is True
    assert audit[0]["cmd"] == ["echo", "wiped"]


def test_reload_registry_picks_up_new_cmd_for_stopped_project(tmp_path: Path) -> None:
    marker = tmp_path / "marker.txt"
    old_cmd = [sys.executable, "-c", f"open({str(marker)!r}, 'w').write('old')"]
    _write_project(tmp_path, "demo", old_cmd)
    engine, events = _engine_with_store(tmp_path)

    engine.start("demo")
    _wait_until(lambda: any(e.type == EventType.FINISHED for e in events))
    assert marker.read_text() == "old"

    new_cmd = [sys.executable, "-c", f"open({str(marker)!r}, 'w').write('new')"]
    _write_project(tmp_path, "demo", new_cmd)
    engine.reload_registry()

    events.clear()
    engine.start("demo")
    _wait_until(lambda: any(e.type == EventType.FINISHED for e in events))
    assert marker.read_text() == "new"


def test_reload_registry_keeps_adapter_for_running_project(tmp_path: Path) -> None:
    _write_project(tmp_path, "demo", [sys.executable, "-c", "import time; time.sleep(30)"])
    engine, events = _engine_with_store(tmp_path)

    engine.start("demo")
    _wait_until(lambda: engine.status("demo").running is True)
    running_adapter = engine._adapters["demo"]

    engine.reload_registry()

    assert engine._adapters["demo"] is running_adapter
    engine.stop("demo")
    _wait_until(lambda: engine.status("demo").running is False)


def test_non_destructive_action_skips_audit(tmp_path: Path) -> None:
    _write_project_with_action(
        tmp_path, "demo", '[[actions]]\nname = "hello"\ncmd = ["echo", "hi"]\n'
    )
    store = EventStore(tmp_path / "warden.db")
    engine = Engine(tmp_path, store=store)
    engine.boot()

    engine.run_action("demo", "hello")

    assert engine.action_audit("demo") == []
