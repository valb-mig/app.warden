"""Event bus interno — motor publica, Notifier/EventStore assinam (fase futura)."""

from collections.abc import Callable

from warden.events import Event

Listener = Callable[[Event], None]


class EventBus:
    def __init__(self) -> None:
        self._listeners: list[Listener] = []

    def subscribe(self, listener: Listener) -> None:
        self._listeners.append(listener)

    def publish(self, event: Event) -> None:
        for listener in self._listeners:
            listener(event)
