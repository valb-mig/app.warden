"""CLI de debug do motor — usa antes de API/front existirem (fase 2)."""

from pathlib import Path

import typer

from warden.config import load_global_config
from warden.engine import Engine
from warden.store import EventStore

app = typer.Typer(help="Warden — CLI de debug do motor")

DEFAULT_CONFIG_DIR = Path.home() / ".warden"
DEFAULT_DB_PATH = DEFAULT_CONFIG_DIR / "warden.db"


def _engine() -> Engine:
    store = EventStore(DEFAULT_DB_PATH)
    engine = Engine(DEFAULT_CONFIG_DIR, store=store)
    engine.boot()
    return engine


@app.command()
def start(project_id: str) -> None:
    _engine().start(project_id)
    typer.echo(f"{project_id}: start disparado")


@app.command()
def stop(project_id: str) -> None:
    _engine().stop(project_id)
    typer.echo(f"{project_id}: stop disparado")


@app.command()
def status(project_id: str) -> None:
    typer.echo(_engine().status(project_id))


@app.command()
def logs(project_id: str, tail: int = 100) -> None:
    for line in _engine().logs(project_id, tail):
        typer.echo(line)


@app.command()
def history(project_id: str, limit: int = 50) -> None:
    for event in _engine().history(project_id, limit):
        typer.echo(f"{event['created_at']}\t{event['type']}\t{event['message']}")


@app.command(name="list")
def list_projects() -> None:
    engine = _engine()
    for project in engine.registry.all():
        typer.echo(f"{project.id}\t{project.type}\t{project.display_name}")


@app.command()
def serve(host: str = "127.0.0.1", port: int | None = None) -> None:
    import uvicorn

    from warden.api.app import create_app

    global_config = load_global_config(DEFAULT_CONFIG_DIR / "config.toml")
    resolved_port = port if port is not None else global_config.api_port
    uvicorn.run(create_app(DEFAULT_CONFIG_DIR), host=host, port=resolved_port)


def main() -> None:
    app()


if __name__ == "__main__":
    main()
