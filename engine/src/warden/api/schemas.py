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
    cpu_percent: float | None = None
    memory_mb: float | None = None


class LogsOut(BaseModel):
    lines: list[str]


class ServicesOut(BaseModel):
    services: list[str]
    error_patterns: list[str] = []


class LanguagesOut(BaseModel):
    languages: list[str]


class HistoryEventOut(BaseModel):
    project_id: str
    type: str
    message: str
    created_at: str


class ActionOut(BaseModel):
    name: str
    interactive: bool
    destructive: bool


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


class ActionAuditOut(BaseModel):
    project_id: str
    action_name: str
    cmd: list[str]
    confirmed: bool
    exit_code: int
    created_at: str


class SystemVitalsOut(BaseModel):
    cpu_percent: float
    memory_percent: float
    memory_used_mb: float
    memory_total_mb: float
    disk_percent: float
    disk_used_gb: float
    disk_total_gb: float
