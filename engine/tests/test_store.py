from pathlib import Path

from warden.events import Event, EventType
from warden.store import EventStore


def test_record_and_history_order(tmp_path: Path) -> None:
    store = EventStore(tmp_path / "warden.db")

    store.record(Event(project_id="demo", type=EventType.STARTED))
    store.record(Event(project_id="demo", type=EventType.ERROR, message="exit=1"))
    store.record(Event(project_id="other", type=EventType.STARTED))

    history = store.history("demo")

    assert len(history) == 2
    assert history[0]["type"] == "error"  # mais recente primeiro
    assert history[0]["message"] == "exit=1"
    assert history[1]["type"] == "started"


def test_history_respects_limit(tmp_path: Path) -> None:
    store = EventStore(tmp_path / "warden.db")
    for _ in range(5):
        store.record(Event(project_id="demo", type=EventType.STARTED))

    assert len(store.history("demo", limit=2)) == 2
