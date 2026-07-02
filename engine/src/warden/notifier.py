"""Notifier plugável — motor dispara evento, canal de envio decide depois (strategy)."""

from abc import ABC, abstractmethod
from urllib.request import Request, urlopen

from warden.config import GlobalConfig, ProjectConfig
from warden.events import Event


class Notifier(ABC):
    @abstractmethod
    def notify(self, event: Event, project: ProjectConfig) -> None: ...


class NullNotifier(Notifier):
    """Default quando nenhum canal está configurado."""

    def notify(self, event: Event, project: ProjectConfig) -> None:
        pass


class NtfyNotifier(Notifier):
    def __init__(self, topic: str, server: str = "https://ntfy.sh"):
        self.topic = topic
        self.server = server.rstrip("/")

    def notify(self, event: Event, project: ProjectConfig) -> None:
        title = f"{project.display_name}: {event.type}"
        body = event.message or event.type.value
        request = Request(
            f"{self.server}/{self.topic}",
            data=body.encode("utf-8"),
            headers={"Title": title},
            method="POST",
        )
        try:
            urlopen(request, timeout=5)
        except OSError:
            pass  # canal secundário — falha de rede não derruba o motor


def create_notifier(global_config: GlobalConfig) -> Notifier:
    if global_config.notify_channel == "none":
        return NullNotifier()
    if global_config.notify_channel == "ntfy":
        if not global_config.ntfy_topic:
            raise ValueError("notify_channel='ntfy' exige ntfy_topic em config.toml")
        return NtfyNotifier(global_config.ntfy_topic, global_config.ntfy_server)
    raise ValueError(f"notify_channel desconhecido: {global_config.notify_channel!r}")
