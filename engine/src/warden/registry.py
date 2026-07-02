"""Registry — carrega e indexa configs de projeto de ~/.warden/projects/*.toml.

Separado de config.toml/api_token/warden.db (arquivos únicos, ficam na raiz de
~/.warden) pra não misturar N tomls de projeto com o resto.
"""

from pathlib import Path

from warden.config import ProjectConfig, load_project_config

PROJECTS_DIRNAME = "projects"


class Registry:
    def __init__(self, config_dir: Path):
        self.config_dir = config_dir
        self.projects_dir = config_dir / PROJECTS_DIRNAME
        self._projects: dict[str, ProjectConfig] = {}

    def load(self) -> None:
        self._projects.clear()
        if not self.projects_dir.exists():
            return
        for toml_file in sorted(self.projects_dir.glob("*.toml")):
            project = load_project_config(toml_file)
            self._projects[project.id] = project

    def get(self, project_id: str) -> ProjectConfig:
        return self._projects[project_id]

    def all(self) -> list[ProjectConfig]:
        return list(self._projects.values())
