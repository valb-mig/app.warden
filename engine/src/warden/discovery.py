"""Descoberta de projetos — varre `scan_paths` configurados por subpastas
ainda não registradas em ~/.warden/projects/*.toml.
"""

from pathlib import Path

from pydantic import BaseModel

from warden.config import GlobalConfig, ProjectType, load_global_config, save_global_config
from warden.registry import Registry
from warden.scaffold import detect_type


class DiscoveredProject(BaseModel):
    name: str
    path: str
    type: ProjectType


class BrowseEntry(BaseModel):
    name: str
    path: str


class BrowseResult(BaseModel):
    path: str
    parent: str | None
    entries: list[BrowseEntry]


def browse_directory(raw_path: str | None) -> BrowseResult:
    target = Path(raw_path).expanduser().resolve() if raw_path else Path.home()
    if not target.is_dir():
        raise ValueError(f"path não é diretório: {target}")

    entries: list[BrowseEntry] = []
    try:
        for entry in sorted(target.iterdir()):
            if entry.name.startswith("."):
                continue
            try:
                if entry.is_dir():
                    entries.append(BrowseEntry(name=entry.name, path=str(entry)))
            except OSError:
                continue
    except PermissionError:
        pass

    parent = str(target.parent) if target.parent != target else None
    return BrowseResult(path=str(target), parent=parent, entries=entries)


def add_scan_path(config_dir: Path, raw_path: str) -> GlobalConfig:
    resolved = Path(raw_path).expanduser().resolve()
    if not resolved.is_dir():
        raise ValueError(f"path não é diretório: {resolved}")

    config_path = config_dir / "config.toml"
    config = load_global_config(config_path)
    resolved_str = str(resolved)
    if resolved_str not in config.scan_paths:
        config.scan_paths.append(resolved_str)
        save_global_config(config_path, config)
    return config


def remove_scan_path(config_dir: Path, raw_path: str) -> GlobalConfig:
    config_path = config_dir / "config.toml"
    config = load_global_config(config_path)
    resolved_str = str(Path(raw_path).expanduser().resolve())
    config.scan_paths = [p for p in config.scan_paths if p != resolved_str]
    save_global_config(config_path, config)
    return config


def discover_projects(config: GlobalConfig, registry: Registry) -> list[DiscoveredProject]:
    registered = {str(Path(p.path).expanduser().resolve()) for p in registry.all()}
    discovered: list[DiscoveredProject] = []
    seen: set[str] = set()

    for raw_scan_path in config.scan_paths:
        scan_path = Path(raw_scan_path).expanduser()
        if not scan_path.is_dir():
            continue
        for entry in sorted(scan_path.iterdir()):
            if not entry.is_dir() or entry.name.startswith("."):
                continue
            resolved = str(entry.resolve())
            if resolved in registered or resolved in seen:
                continue
            seen.add(resolved)
            discovered.append(
                DiscoveredProject(name=entry.name, path=resolved, type=detect_type(entry))
            )
    return discovered
