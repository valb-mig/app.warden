"""Endpoints de sincronização de projetos — gerência de scan_paths + a versão
web do `warden init` (preview do que seria detectado, aplicar grava o toml).
"""

from fastapi import APIRouter, Depends, HTTPException
from pydantic import BaseModel

from warden.api.deps import get_engine
from warden.config import ProjectConfig, load_global_config, load_project_config
from warden.discovery import (
    BrowseResult,
    DiscoveredProject,
    add_scan_path,
    browse_directory,
    discover_projects,
    remove_scan_path,
)
from warden.engine import Engine
from warden.registry import PROJECTS_DIRNAME
from warden.scaffold import build_config, render_toml

router = APIRouter(tags=["discovery"])


class ScanPathsOut(BaseModel):
    scan_paths: list[str]


class ScanPathIn(BaseModel):
    path: str


class DiscoverOut(BaseModel):
    projects: list[DiscoveredProject]


class PreviewIn(BaseModel):
    path: str
    id: str | None = None


class ConfigOut(BaseModel):
    config: ProjectConfig
    toml: str


@router.get("/scan-paths", response_model=ScanPathsOut)
def get_scan_paths(engine: Engine = Depends(get_engine)) -> ScanPathsOut:
    config = load_global_config(engine.registry.config_dir / "config.toml")
    return ScanPathsOut(scan_paths=config.scan_paths)


@router.post("/scan-paths", response_model=ScanPathsOut)
def add_scan_path_route(body: ScanPathIn, engine: Engine = Depends(get_engine)) -> ScanPathsOut:
    try:
        config = add_scan_path(engine.registry.config_dir, body.path)
    except ValueError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from None
    return ScanPathsOut(scan_paths=config.scan_paths)


@router.delete("/scan-paths", response_model=ScanPathsOut)
def remove_scan_path_route(body: ScanPathIn, engine: Engine = Depends(get_engine)) -> ScanPathsOut:
    config = remove_scan_path(engine.registry.config_dir, body.path)
    return ScanPathsOut(scan_paths=config.scan_paths)


@router.get("/browse", response_model=BrowseResult)
def browse(path: str | None = None) -> BrowseResult:
    try:
        return browse_directory(path)
    except ValueError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from None


@router.get("/discover", response_model=DiscoverOut)
def discover(engine: Engine = Depends(get_engine)) -> DiscoverOut:
    config = load_global_config(engine.registry.config_dir / "config.toml")
    return DiscoverOut(projects=discover_projects(config, engine.registry))


@router.post("/discover/preview", response_model=ConfigOut)
def preview(body: PreviewIn) -> ConfigOut:
    try:
        config = build_config(body.path, project_id=body.id)
    except FileNotFoundError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from None
    return ConfigOut(config=config, toml=render_toml(config))


@router.post("/discover/apply", response_model=ConfigOut)
def apply(body: ProjectConfig, engine: Engine = Depends(get_engine)) -> ConfigOut:
    toml_text = render_toml(body)
    target = engine.registry.config_dir / PROJECTS_DIRNAME / f"{body.id}.toml"
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(toml_text)
    engine.reload_registry()
    return ConfigOut(config=body, toml=toml_text)


@router.get("/projects/{project_id}/config", response_model=ProjectConfig)
def get_project_config(project_id: str, engine: Engine = Depends(get_engine)) -> ProjectConfig:
    target = engine.registry.config_dir / PROJECTS_DIRNAME / f"{project_id}.toml"
    if not target.exists():
        raise HTTPException(status_code=404, detail=f"config de {project_id!r} não encontrada")
    return load_project_config(target)
