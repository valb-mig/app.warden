import sys
import time
from pathlib import Path

from warden.engine import Engine
from warden.events import EventType
from warden.store import EventStore


def _wait_until(predicate, timeout: float = 3.0) -> None:
    deadline = time.time() + timeout
    while time.time() < deadline:
        if predicate():
            return
        time.sleep(0.05)
    raise AssertionError("condição não satisfeita dentro do timeout")


def _write_project(config_dir: Path, project_id: str, cmd: list[str]) -> None:
    (config_dir / f"{project_id}.toml").write_text(
        f'id = "{project_id}"\ntype = "raw"\npath = "{config_dir}"\n\n[start]\ncmd = {cmd!r}\n'
    )


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
