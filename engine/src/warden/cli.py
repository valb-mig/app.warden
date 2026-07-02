"""CLI de debug do motor — usa antes de API/front existirem (fase 2)."""

from pathlib import Path

import typer

from warden.config import load_global_config
from warden.engine import Engine
from warden.registry import PROJECTS_DIRNAME
from warden.scaffold import build_config, render_toml
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
def init(
    path: str,
    id: str | None = typer.Option(None, "--id", help="id do projeto (default: slug da pasta)"),
    yes: bool = typer.Option(False, "--yes", "-y", help="pula confirmação e grava direto"),
) -> None:
    """Detecta tipo de projeto na pasta e gera ~/.warden/<id>.toml."""
    try:
        config = build_config(path, project_id=id)
    except FileNotFoundError as exc:
        typer.echo(str(exc), err=True)
        raise typer.Exit(1) from exc

    toml_text = render_toml(config)
    typer.echo(f"tipo detectado: {config.type}")
    typer.echo(toml_text)

    target = DEFAULT_CONFIG_DIR / PROJECTS_DIRNAME / f"{config.id}.toml"
    if target.exists():
        typer.echo(f"aviso: {target} já existe, será sobrescrito")

    if not yes and not typer.confirm(f"Gravar em {target}?"):
        typer.echo("cancelado")
        raise typer.Exit(0)

    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(toml_text)
    typer.echo(f"gravado: {target}")


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
