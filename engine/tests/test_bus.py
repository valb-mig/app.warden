from warden.bus import EventBus
from warden.events import Event, EventType


def test_publish_calls_all_subscribers() -> None:
    received = []
    bus = EventBus()
    bus.subscribe(received.append)
    bus.subscribe(lambda e: received.append(e))

    event = Event(project_id="demo", type=EventType.STARTED)
    bus.publish(event)

    assert received == [event, event]
