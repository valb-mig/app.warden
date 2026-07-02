import sys
import time
from pathlib import Path

from warden.engine import Engine
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
