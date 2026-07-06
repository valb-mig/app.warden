"""Config loader — TOML por projeto (~/.warden/<id>.toml) + config global."""

import json
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
    on_git_behind: bool = False


class GitWatchConfig(BaseModel):
    watch: bool = False
    interval: float = 300.0
    remote: str = "origin"


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
    destructive: bool = False


class ProjectConfig(BaseModel):
    id: str
    name: str | None = None
    group: str | None = None
    path: str
    type: ProjectType
    compose_file: str | None = None
    compose_services: list[str] = Field(default_factory=list)
    start: StartConfig | None = None
    notify: NotifyConfig = Field(default_factory=NotifyConfig)
    git: GitWatchConfig = Field(default_factory=GitWatchConfig)
    log_sources: list[LogSource] = Field(default_factory=list)
    actions: list[ActionConfig] = Field(default_factory=list)

    @property
    def display_name(self) -> str:
        return self.name or self.id


class GlobalConfig(BaseModel):
    api_port: int = 8420
    notify_channel: Literal["none", "ntfy"] = "none"
    ntfy_topic: str | None = None
    ntfy_server: str = "https://ntfy.sh"
    scan_paths: list[str] = Field(default_factory=list)


def load_project_config(path: Path) -> ProjectConfig:
    data = tomllib.loads(path.read_text())
    return ProjectConfig.model_validate(data)


def render_global_config_toml(config: GlobalConfig) -> str:
    ntfy_topic_line = (
        f'ntfy_topic = "{config.ntfy_topic}"'
        if config.ntfy_topic
        else '# ntfy_topic = "warden-alerts"'
    )
    return f"""\
# Config global do daemon Warden. Todo campo aqui já tem esse valor por
# default no código — esse arquivo só existe pra deixar visível o que dá
# pra mudar, sem precisar ler o código-fonte.

# Porta onde a API (FastAPI + WebSocket) sobe.
api_port = {config.api_port}

# Canal de notificação de eventos (started/stopped/finished/error): "none" ou "ntfy".
notify_channel = "{config.notify_channel}"

# Obrigatório se notify_channel = "ntfy" (sem default — precisa descomentar e preencher).
{ntfy_topic_line}

# Server do ntfy — só muda se for self-hosted.
ntfy_server = "{config.ntfy_server}"

# Pastas onde o Warden procura projetos candidatos (subpastas diretas) ao
# sincronizar pelo front — gerenciado por lá, mas editar aqui também vale.
scan_paths = {json.dumps(config.scan_paths)}
"""


def load_global_config(path: Path) -> GlobalConfig:
    if not path.exists():
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(render_global_config_toml(GlobalConfig()))
        return GlobalConfig()
    data = tomllib.loads(path.read_text())
    return GlobalConfig.model_validate(data)


def save_global_config(path: Path, config: GlobalConfig) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(render_global_config_toml(config))
