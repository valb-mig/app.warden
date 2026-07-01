from dataclasses import dataclass
from enum import StrEnum


class EventType(StrEnum):
    STARTED = "started"
    STOPPED = "stopped"
    FINISHED = "finished"
    ERROR = "error"


@dataclass
class Event:
    project_id: str
    type: EventType
    message: str = ""
