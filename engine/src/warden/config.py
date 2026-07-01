"""Config loader — TOML por projeto (~/.warden/<id>.toml) + config global."""

import tomllib
from pathlib import Path
from typing import Literal

from pydantic import BaseModel, Field

ProjectType = Literal["docker", "node", "python", "php", "just", "raw"]


class StartConfig(BaseModel):
    cmd: list[str]
    cwd: str | None = None
    capture_stdout: bool = False


class NotifyConfig(BaseModel):
    on_error: bool = False
    on_finished: bool = False


class LogSource(BaseModel):
    name: str
    type: Literal["stdout", "file", "docker"]
    path: str | None = None
    service: str | None = None
    error_patterns: list[str] = Field(default_factory=list)


class ActionConfig(BaseModel):
    name: str
    cmd: list[str]
    interactive: bool = False


class ProjectConfig(BaseModel):
    id: str
    name: str | None = None
    group: str | None = None
    path: str
    type: ProjectType
    compose_file: str | None = None
    start: StartConfig | None = None
    notify: NotifyConfig = Field(default_factory=NotifyConfig)
    log_sources: list[LogSource] = Field(default_factory=list)
    actions: list[ActionConfig] = Field(default_factory=list)

    @property
    def display_name(self) -> str:
        return self.name or self.id


class GlobalConfig(BaseModel):
    api_port: int = 8420
    notify_channel: str | None = None


def load_project_config(path: Path) -> ProjectConfig:
    data = tomllib.loads(path.read_text())
    return ProjectConfig.model_validate(data)


def load_global_config(path: Path) -> GlobalConfig:
    if not path.exists():
        return GlobalConfig()
    data = tomllib.loads(path.read_text())
    return GlobalConfig.model_validate(data)
