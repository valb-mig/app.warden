"""Registry — carrega e indexa configs de projeto de ~/.warden/*.toml."""

from pathlib import Path

from warden.config import ProjectConfig, load_project_config


class Registry:
    def __init__(self, config_dir: Path):
        self.config_dir = config_dir
        self._projects: dict[str, ProjectConfig] = {}

    def load(self) -> None:
        self._projects.clear()
        if not self.config_dir.exists():
            return
        for toml_file in sorted(self.config_dir.glob("*.toml")):
            if toml_file.name == "config.toml":
                continue
            project = load_project_config(toml_file)
            self._projects[project.id] = project

    def get(self, project_id: str) -> ProjectConfig:
        return self._projects[project_id]

    def all(self) -> list[ProjectConfig]:
        return list(self._projects.values())
