from pydantic import BaseModel

from warden.config import ProjectType


class ProjectOut(BaseModel):
    id: str
    name: str
    type: ProjectType
    group: str | None = None


class StatusOut(BaseModel):
    running: bool
    pid: int | None = None
    ports: list[int] = []
    uptime_seconds: float | None = None


class LogsOut(BaseModel):
    lines: list[str]


class ServicesOut(BaseModel):
    services: list[str]


class HistoryEventOut(BaseModel):
    project_id: str
    type: str
    message: str
    created_at: str


class ActionOut(BaseModel):
    name: str
    interactive: bool


class ActionResultOut(BaseModel):
    exit_code: int
    output: str


class GitCommitOut(BaseModel):
    hash: str
    subject: str
    author: str
    relative: str


class GitInfoOut(BaseModel):
    branch: str
    dirty: bool
    dirty_count: int
    ahead: int | None = None
    behind: int | None = None
    has_remote: bool
    last_commit: GitCommitOut | None = None


class GitCommandResultOut(BaseModel):
    ok: bool
    exit_code: int
    output: str
    refused: bool
