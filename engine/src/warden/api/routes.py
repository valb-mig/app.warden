from fastapi import APIRouter, Depends, HTTPException

from warden.api.deps import get_engine
from warden.api.schemas import (
    ActionOut,
    ActionResultOut,
    HistoryEventOut,
    LogsOut,
    ProjectOut,
    ServicesOut,
    StatusOut,
)
from warden.config import ProjectConfig
from warden.engine import Engine

router = APIRouter(prefix="/projects", tags=["projects"])


def _project_or_404(engine: Engine, project_id: str) -> ProjectConfig:
    try:
        return engine.registry.get(project_id)
    except KeyError:
        raise HTTPException(
            status_code=404, detail=f"projeto {project_id!r} não encontrado"
        ) from None


@router.get("", response_model=list[ProjectOut])
def list_projects(engine: Engine = Depends(get_engine)) -> list[ProjectOut]:
    return [
        ProjectOut(id=p.id, name=p.display_name, type=p.type, group=p.group)
        for p in engine.registry.all()
    ]


@router.post("/{project_id}/start")
def start_project(project_id: str, engine: Engine = Depends(get_engine)) -> dict:
    _project_or_404(engine, project_id)
    engine.start(project_id)
    return {"ok": True}


@router.post("/{project_id}/stop")
def stop_project(project_id: str, engine: Engine = Depends(get_engine)) -> dict:
    _project_or_404(engine, project_id)
    engine.stop(project_id)
    return {"ok": True}


@router.get("/{project_id}/status", response_model=StatusOut)
def project_status(project_id: str, engine: Engine = Depends(get_engine)) -> StatusOut:
    _project_or_404(engine, project_id)
    s = engine.status(project_id)
    return StatusOut(running=s.running, pid=s.pid, ports=s.ports, uptime_seconds=s.uptime_seconds)


@router.get("/{project_id}/logs", response_model=LogsOut)
def project_logs(
    project_id: str,
    tail: int = 100,
    service: str | None = None,
    engine: Engine = Depends(get_engine),
) -> LogsOut:
    _project_or_404(engine, project_id)
    return LogsOut(lines=engine.logs(project_id, tail, service=service))


@router.get("/{project_id}/services", response_model=ServicesOut)
def project_services(project_id: str, engine: Engine = Depends(get_engine)) -> ServicesOut:
    _project_or_404(engine, project_id)
    return ServicesOut(services=engine.services(project_id))


@router.get("/{project_id}/history", response_model=list[HistoryEventOut])
def project_history(
    project_id: str, limit: int = 50, engine: Engine = Depends(get_engine)
) -> list[dict]:
    _project_or_404(engine, project_id)
    return engine.history(project_id, limit)


@router.get("/{project_id}/actions", response_model=list[ActionOut])
def list_actions(project_id: str, engine: Engine = Depends(get_engine)) -> list[ActionOut]:
    project = _project_or_404(engine, project_id)
    return [ActionOut(name=a.name, interactive=a.interactive) for a in project.actions]


@router.post("/{project_id}/actions/{action_name}", response_model=ActionResultOut)
def run_action(
    project_id: str, action_name: str, engine: Engine = Depends(get_engine)
) -> ActionResultOut:
    _project_or_404(engine, project_id)
    try:
        result = engine.run_action(project_id, action_name)
    except KeyError as exc:
        raise HTTPException(status_code=404, detail=str(exc)) from None
    except ValueError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from None
    return ActionResultOut(exit_code=result.exit_code, output=result.output)
